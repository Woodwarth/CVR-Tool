// Application / CVR LAN backend
// Purpose: expose observed amplifier settings through the common backend contract.
// Input: explicit target only; wire function mappings are documented separately.
// State: serialize operations, invalidate plans on observed changes and every write attempt.
// Scope: writes require readback; failed multi-parameter writes may have partially applied.
using System.Text;
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrLanBackend : IAmplifierBackend, INativePresetMetadataSource, IDisposable
{
    private readonly CvrUdpClient client;
    private readonly object gate = new();
    private readonly Dictionary<string, byte[]> observations = new();
    private long revision;
    private readonly DeviceCapabilities capabilities;
    private readonly string id;
    private string name;
    private DeviceSnapshot? lastSnapshot;
    public CvrLanBackend(NetworkTarget target)
    {
        client = new(target);
        id = $"cvr:{target.RemoteAddress}:{target.RemotePort}";
        try
        {
            var basic = Read(0);
            if (basic.Length is not (75 or 79) || basic[73] is not (2 or 4) || basic[71] is not (2 or 4))
                throw new NotSupportedException("Unsupported CVR identity/topology response.");
            capabilities = new(Text(basic[..32]), basic[73], basic[71], false);
            name = Text(basic[32..64]);
            if (string.IsNullOrWhiteSpace(name))
                name = capabilities.Model;
            // No default channel values are exposed for a physical device.
            GetDevice(id);
        }
        catch { client.Dispose(); throw; }
    }
    private static string Text(byte[] bytes) => Encoding.Latin1.GetString(bytes.TakeWhile(b => b != 0).ToArray()).Trim();
    private void CheckId(string value)
    {
        if (value != id)
            throw new ArgumentException("Unknown connected device.");
    }
    private byte Channel(int channel, bool input = false)
    {
        if (channel < 1 || channel > (input ? capabilities.InputCount : capabilities.OutputCount))
            throw new ArgumentOutOfRangeException(nameof(channel));
        return (byte)(channel - 1);
    }
    private byte[] Read(byte function, byte channel = 0, byte segment = 0, byte side = 0)
    {
        var bytes = client.Read(function, channel, segment, side);
        var key = $"{function}/{channel}/{segment}/{side}";
        if (observations.TryGetValue(key, out var before) && !before.SequenceEqual(bytes))
            revision++;
        observations[key] = bytes.ToArray();
        return bytes;
    }
    private void Expected(long expected)
    {
        if (revision != expected)
            throw new InvalidOperationException("Device changed. Create a new plan.");
    }
    private void Write(byte function, byte[] body, byte channel = 0, byte segment = 0, byte side = 0)
    {
        revision++; // Also invalidates plans when readback fails after a potentially successful write.
        client.Write(function, body, channel, segment, side, messageStatus: function is 15 or 49 ? (byte)0 : (byte)1);
        var observed = Read(function == 60 ? (byte)0 : function, channel, segment, side);
        if (function == 60)
            observed = observed.Length >= 64 ? observed[32..64] : [];
        if (!observed.SequenceEqual(body))
            throw new InvalidOperationException("Write readback differs. The device may have changed partially; read fresh settings before retrying.");
    }
    private static double Float(byte[] bytes, int offset = 0)
    {
        if (bytes.Length < offset + 4)
            throw new InvalidDataException("Truncated CVR float.");
        var value = BitConverter.ToSingle(bytes, offset);
        if (!float.IsFinite(value))
            throw new InvalidDataException("Non-finite CVR reading.");
        return value;
    }
    private static byte[] FloatBytes(double value) => BitConverter.GetBytes((float)value);
    private static bool Flag(byte[] bytes)
    {
        if (bytes.Length != 1 || bytes[0] > 1)
            throw new NotSupportedException("Unsupported CVR boolean state.");
        return bytes[0] == 1;
    }
    public IReadOnlyList<DeviceSnapshot> ListDevices()
    {
        lock (gate)
            return [lastSnapshot! with { Outputs = lastSnapshot!.Outputs.ToArray() }];
    }
    public DeviceSnapshot GetDevice(string deviceId)
    {
        lock (gate)
        {
            CheckId(deviceId);
            var identity = Read(0);
            if (identity.Length is not (75 or 79))
                throw new InvalidDataException("Invalid CVR identity length.");
            name = Text(identity[32..64]);
            if (string.IsNullOrWhiteSpace(name))
                name = capabilities.Model;
            lastSnapshot = new(id, name, capabilities, false, DateTimeOffset.UtcNow,
                Enumerable.Range(1, capabilities.OutputCount).Select(n => ReadChannel(n)).ToArray());
            return lastSnapshot with
            {
                Outputs = lastSnapshot.Outputs.ToArray()
            };
        }
    }
    private ChannelState ReadChannel(int channel)
    {
        var wire = Channel(channel);
        return new(channel, ((char)('A' + channel - 1)).ToString(), Float(Read(9, wire, side: 1)),
            !Flag(Read(10, wire, side: 1)), Float(Read(14, wire, side: 1)), Flag(Read(18, wire, side: 1)));
    }
    public (ChannelState State, long Revision) GetChannel(string deviceId, int channel)
    {
        lock (gate)
        {
            CheckId(deviceId);
            var state = ReadChannel(channel);
            return (state, revision);
        }
    }
    public ChannelState Apply(ChannelChange change, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(change.DeviceId);
            var before = ReadChannel(change.Channel);
            Expected(expectedRevision);
            var after = AmplifierService.Merge(before, change);
            var ch = Channel(change.Channel);
            if (change.GainDb is not null)
                Write(9, FloatBytes(after.GainDb), ch, side: 1);
            if (change.Muted is not null)
                Write(10, [(byte)(after.Muted ? 0 : 1)], ch, side: 1);
            if (change.DelayMs is not null)
                Write(14, FloatBytes(after.DelayMs), ch, side: 1);
            if (change.Inverted is not null)
                Write(18, [(byte)(after.Inverted ? 1 : 0)], ch, side: 1);
            return ReadChannel(change.Channel);
        }
    }
    public RoutingSnapshot GetRouting(string deviceId)
    {
        lock (gate)
        {
            CheckId(deviceId);
            var cells = new List<RoutingCellState>();
            for (var output = 1; output <= capabilities.OutputCount; output++)
                for (var input = 1; input <= capabilities.InputCount; input++)
                    cells.Add(ReadRoute(input, output));
            return new(id, capabilities.InputCount!.Value, capabilities.OutputCount, cells.ToArray(), revision);
        }
    }
    private RoutingCellState ReadRoute(int input, int output)
    {
        // Captured off-diagonal route: channel=output, segment=input.
        var body = Read(12, Channel(output), Channel(input, true));
        if (body.Length != 5)
            throw new InvalidDataException("Invalid CVR route response.");
        return new(input, output, Float(body), !Flag(body[4..]));
    }
    public RoutingCellState ApplyRouting(RoutingChange change, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(change.DeviceId);
            var before = ReadRoute(change.Input, change.Output);
            Expected(expectedRevision);
            var after = AmplifierService.MergeRouting(before, change);
            Write(12, [.. FloatBytes(after.GainDb), (byte)(after.Muted ? 0 : 1)], Channel(change.Output), Channel(change.Input, true));
            return ReadRoute(change.Input, change.Output);
        }
    }
    public DeviceSnapshot Rename(string deviceId, string value, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(deviceId);
            Read(0);
            Expected(expectedRevision);
            value = AmplifierService.NormalizeDeviceName(value);
            if (value.Any(c => c > 127))
                throw new ArgumentException("Device-side names currently require ASCII characters (up to 32 bytes).");
            var body = new byte[32];
            Encoding.ASCII.GetBytes(value).CopyTo(body, 0);
            Write(60, body);
            name = value;
            return GetDevice(id);
        }
    }
    public DeviceControlState GetControlState(string deviceId)
    {
        lock (gate)
        {
            CheckId(deviceId);
            return new(Flag(Read(17)), Flag(Read(15)));
        }
    }
    public DeviceControlState SetControlState(string deviceId, bool? displayLocked, bool? standby, long expectedRevision)
    {
        lock (gate)
        {
            if (displayLocked is null && standby is null)
                throw new ArgumentException("Supply a control state.");
            GetControlState(deviceId);
            Expected(expectedRevision);
            if (displayLocked is { } locked)
                Write(17, [(byte)(locked ? 1 : 0)]);
            if (standby is { } sleep)
                Write(15, [(byte)(sleep ? 1 : 0)]);
            return GetControlState(id);
        }
    }
    public void Dispose() => client.Dispose();
}

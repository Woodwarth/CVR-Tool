// Application / Observed live reads
// Purpose: expose full native settings and periodic status beside local devices.
// Units: native DSP units; telemetry timestamps are independent of settings age.
// State: one exclusive UDP owner, serialized polling and cached immutable reads.
// Scope: only evidenced write variants are enabled; every edit requires independent readback.
using System.Text;
using System.Text.Json;
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend : IAmplifierBackend, IAdvancedAmplifierBackend, INativePresetMetadataSource, IDisposable
{
    private readonly NetworkTarget target;
    private byte[]? identityBody;
    private readonly object gate = new();
    private readonly object ioGate = new();
    private readonly string id;
    private readonly Timer timer;
    private PresetDocument settings = null!;
    private byte[] native = [];
    private OutputTelemetry[] telemetry = [];
    private DeviceSnapshot device = null!;
    private DateTimeOffset settingsAt, telemetryAt;
    private double? power;
    private int[]? statusCodes;
    private string? error;
    private long revision;
    private int polling;
    private bool disposed;
    private bool forceSettingsRefresh;
    private bool standbyState;
    private bool standbyKnown;
    private uint? operatingMinutes;
    private DateTimeOffset metadataCheckedAt;

    public CvrSnapshotBackend(NetworkTarget target)
    {
        id = $"cvr:{target.RemoteAddress}:{target.RemotePort}";
        this.target = target;
        CvrEndpointGate.UseClient(target, client =>
        {
            try { client.ReadSnapshot(0); } catch (TimeoutException) { }
            ReadSettings(client);
            ReadMetadata(client);
            ReadTelemetry(client);
            identityBody = client.ObservedIdentity;
            UpdateSnapshot();
        });
        timer = new Timer(_ => Poll(), null, 500, 500);
    }

    private static string Text(byte[] data) => Encoding.Latin1.GetString(data.TakeWhile(b => b != 0).ToArray()).Trim();
    private void CheckId(string value)
    {
        if (value != id) throw new ArgumentException("Unknown connected device.");
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    private void CheckChannel(int channel)
    {
        if (channel < 1 || channel > settings.Outputs.Count) throw new ArgumentOutOfRangeException(nameof(channel));
    }
    private void ReadSettings(CvrUdpClient client)
    {
        var bytes = client.ReadSnapshot(27);
        var parsed = NativePresetCodec.Decode(bytes);
        if (bytes[33] > 1) throw new NotSupportedException("Unknown device control flags.");
        lock (gate)
        {
            if (native.Length != 0 && (settings.Model != parsed.Model || settings.Outputs.Count != parsed.Outputs.Count))
                throw new NotSupportedException("Device identity/topology changed; reconnect explicitly.");
            if (native.Length != 0 && !native.SequenceEqual(bytes)) revision++;
            native = bytes;
            settings = parsed;
            settingsAt = DateTimeOffset.UtcNow;
            forceSettingsRefresh = false;
        }
    }
    private void ReadMetadata(CvrUdpClient client)
    {
        try
        {
            ReadWink(client);
            var body = client.ReadSnapshot(71);
            string header; lock (gate) header = Text(native[..32]);
            var minutes = DeviceMetadataCodec.OperatingMinutes(body, header);
            lock (gate) operatingMinutes = minutes;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or NotSupportedException) { }
        finally { metadataCheckedAt = DateTimeOffset.UtcNow; }
    }
    private void ReadTelemetry(CvrUdpClient client)
    {
        var bytes = client.ReadSnapshot(6);
        int count; lock (gate) count = settings.Outputs.Count;
        var readings = DecodeTelemetry(bytes, count);
        var reportedPower = bytes.Length == 32 ? (double?)null : ProcessingWire.Float(bytes, 16);
        var offset = bytes.Length == 32 ? 20 : 52;
        var codes = Enumerable.Range(0, count).Select(i => (int)bytes[offset + i]).ToArray();
        lock (gate)
        {
            telemetry = readings; power = reportedPower; statusCodes = codes;
            standbyKnown = codes.All(code => code <= 11);
            if (standbyKnown) standbyState = codes.All(code => code == 1);
            telemetryAt = DateTimeOffset.UtcNow;
        }
    }

    public static OutputTelemetry[] DecodeTelemetry(byte[] bytes, int count)
    {
        if (count is not (2 or 4) || (bytes.Length == 32 ? count != 2 : bytes.Length is not (56 or 72 or 76 or 88 or 92 or 96)))
            throw new NotSupportedException("Unsupported live telemetry layout.");
        return Enumerable.Range(0, count).Select(i =>
        {
            var compact = bytes.Length == 32;
            var voltage = ProcessingWire.Float(bytes, (compact ? 4 : 20) + i * 4);
            var current = ProcessingWire.Float(bytes, (compact ? 12 : 36) + i * 4);
            var code = bytes[(compact ? 20 : 52) + i];
            var flags = code <= 11 ? new OutputStatus(Fault: code is 2 or 6 or 7,
                Load: code == 4, Open: compact ? null : code == 3, OverTemperature: code == 9,
                Clip: code == 5, Standby: code == 1) : new OutputStatus();
            return new OutputTelemetry(i + 1, Voltage: voltage, Current: current,
                LoadOhms: current > 0 ? voltage / current : null,
                TemperatureC: compact ? null : ProcessingWire.Float(bytes, i * 4), Status: flags);
        }).ToArray();
    }
    private void UpdateSnapshot()
    {
        var identity = identityBody;
        var name = identity is not null ? Text(identity[32..64]) : "—";
        var mac = identity is not null ? string.Join(":", identity[64..70].Select(b => b.ToString("X2"))) : null;
        var currentPreset = native.Length is 2232 or 2687 ? Text(native[2160..2192]) : null;
        var readings = new DeviceReadings(true, Text(native[..32]), mac,
            string.IsNullOrEmpty(currentPreset) ? null : currentPreset, settingsAt,
            telemetryAt == default ? null : telemetryAt, power, statusCodes?.ToArray(), error, CanSetDisplayLock: true, CanSetStandby: standbyKnown, CanSetMutes: true, OperatingMinutes: operatingMinutes, CanSetWink: winkState is not null, CanSetInputGain: true, CanSetInputMute: settings.Inputs!.All(i => i.Muted is not null), CanRename: true, CanSetVerifiedDsp: true);
        device = new(id, string.IsNullOrWhiteSpace(name) ? "—" : name,
            new(settings.Model, settings.Outputs.Count, settings.Outputs.Count, false), false,
            settingsAt, settings.Outputs.ToArray(), readings);
    }
    private void Poll()
    {
        if (Interlocked.Exchange(ref polling, 1) != 0) return;
        try
        {
            lock (ioGate)
            {
                lock (gate) { if (disposed) return; }
                try
                {
                    DateTimeOffset observed; lock (gate) observed = settingsAt;
                    CvrEndpointGate.UseClient(target, client =>
                    {
                        if (forceSettingsRefresh || DateTimeOffset.UtcNow - observed >= TimeSpan.FromSeconds(5)) ReadSettings(client);
                        ReadTelemetry(client);
                        if (winkState is not null && DateTimeOffset.UtcNow - winkCheckedAt >= TimeSpan.FromSeconds(5)) ReadWink(client);
                        if (DateTimeOffset.UtcNow - metadataCheckedAt >= TimeSpan.FromMinutes(1)) ReadMetadata(client);
                        if (client.ObservedIdentity is { } identity) lock (gate) identityBody = identity;
                    });
                    lock (gate) { error = null; UpdateSnapshot(); }
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or NotSupportedException or System.Net.Sockets.SocketException)
                { lock (gate) { error = ex.Message; UpdateSnapshot(); } }
            }
        }
        finally { Volatile.Write(ref polling, 0); }
    }
    private DeviceSnapshot CopyDevice() => device with
    {
        Outputs = device.Outputs.ToArray(),
        Readings = device.Readings! with { StatusCodes = statusCodes?.ToArray() }
    };
    private static InputProcessingState CopyProcessing(InputProcessingState value) => value with { Bands = value.Bands.ToArray() };
    public IReadOnlyList<DeviceSnapshot> ListDevices() { lock (gate) return [CopyDevice()]; }
    public DeviceSnapshot GetDevice(string deviceId) { lock (gate) { CheckId(deviceId); return CopyDevice(); } }
    public (ChannelState State, long Revision) GetChannel(string deviceId, int channel)
    { lock (gate) { CheckId(deviceId); CheckChannel(channel); return (settings.Outputs.Single(c => c.Number == channel), revision); } }
    public IReadOnlyList<OutputTelemetry> GetTelemetry(string deviceId)
    { lock (gate) { CheckId(deviceId); return telemetry.ToArray(); } }
    public RoutingSnapshot GetRouting(string deviceId)
    { lock (gate) { CheckId(deviceId); return new(id, settings.Outputs.Count, settings.Outputs.Count, settings.Routing!.ToArray(), revision); } }
    public (InputProcessingState State, long Revision) GetInputProcessing(string deviceId, int channel)
    { lock (gate) { CheckId(deviceId); CheckChannel(channel); return (CopyProcessing(settings.Inputs!.Single(c => c.Channel == channel)), revision); } }
    public (InputProcessingState State, long Revision) GetOutputProcessing(string deviceId, int channel)
    { lock (gate) { CheckId(deviceId); CheckChannel(channel); return (CopyProcessing(settings.OutputProcessing!.Single(c => c.Channel == channel)), revision); } }
    public (OutputDynamicsState State, long Revision) GetOutputDynamics(string deviceId, int channel)
    { lock (gate) { CheckId(deviceId); CheckChannel(channel); return (settings.Dynamics!.Single(c => c.Channel == channel), revision); } }
    public (FirState State, long Revision) GetFir(string deviceId, int channel)
    { lock (gate) { CheckId(deviceId); CheckChannel(channel); return (new(channel, "Unknown", [], native[36 + (channel - 1) * 515 + 368] == 0, CoefficientsKnown: false), revision); } }
    public DeviceControlState GetControlState(string deviceId)
    { lock (gate) { CheckId(deviceId); return new(native[33] != 0, standbyState, StandbyKnown: standbyKnown, Wink: winkState); } }
    public DevicePresetState GetDevicePresets(string deviceId)
    { lock (gate) { CheckId(deviceId); return new([], device.Readings?.CurrentPreset); } }
    public NativePresetMetadata GetNativePresetMetadata(string deviceId)
    { lock (gate) { CheckId(deviceId); return NativePresetMetadata.Decode(native); } }
    public PresetDocument CapturePreset(string deviceId)
    { lock (gate) { CheckId(deviceId); return JsonSerializer.Deserialize<PresetDocument>(JsonSerializer.Serialize(settings with { Name = device.Name }))!; } }

    private static NotSupportedException ReadOnly() => new("Only physically evidenced command variants are writable on this live connection.");
    public InputProcessingState ApplyOutputProcessing(InputProcessingChange change, long expectedRevision) => throw ReadOnly();
    public FirState ApplyFir(string deviceId, FirState state, long expectedRevision) => throw ReadOnly();
    public DeviceSnapshot ApplyPreset(string deviceId, PresetDocument preset, long expectedRevision) => throw ReadOnly();
    public DeviceOperationResult StoreDevicePreset(string deviceId, int slot, string name, long expectedRevision) => throw ReadOnly();
    public DeviceOperationResult RecallDevicePreset(string deviceId, int slot, long expectedRevision) => throw ReadOnly();
    public DeviceOperationResult FactoryReset(string deviceId, long expectedRevision) => throw ReadOnly();
    public DeviceOperationResult ClearDevicePresets(string deviceId, long expectedRevision) => throw ReadOnly();
    public void Dispose()
    {
        timer.Dispose();
        lock (ioGate)
        {
            lock (gate) disposed = true;
        }
    }
}

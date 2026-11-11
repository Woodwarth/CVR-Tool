// Application / Verified output and route muting
// Purpose: allow active-low mute flags while preserving every unrelated DSP byte.
// Units: function 10 output side 1; function 12 output/channel, input/segment side 0.
// State: fresh full settings protect revisions and independently verify each write.
// Scope: no gain changes, automatic write retries or implicit route enabling.
using System.Diagnostics;
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend
{
    public ChannelState Apply(ChannelChange change, long expectedRevision)
    {
        if (change.Muted is null || change.GainDb is not null || change.DelayMs is not null || change.Inverted is not null) throw ReadOnly();
        MutateMute(change.DeviceId, expectedRevision, client =>
        {
            CheckChannel(change.Channel);
            var offset = 36 + (change.Channel - 1) * 515 + 48;
            WriteMuteFlag(client, 10, (byte)(change.Channel - 1), 0, 1, offset, change.Muted.Value, false);
        });
        return GetChannel(change.DeviceId, change.Channel).State;
    }
    public RoutingCellState ApplyRouting(RoutingChange change, long expectedRevision)
    {
        if (change.Muted is null || change.GainDb is not null) throw ReadOnly();
        MutateMute(change.DeviceId, expectedRevision, client =>
        {
            CheckChannel(change.Input); CheckChannel(change.Output);
            var offset = 36 + (change.Output - 1) * 515 + 28 + (change.Input - 1) * 5;
            WriteMuteFlag(client, 12, (byte)(change.Output - 1), (byte)(change.Input - 1), 0, offset, change.Muted.Value, true);
        });
        return GetRouting(change.DeviceId).Cells.Single(c => c.Input == change.Input && c.Output == change.Output);
    }
    private void MutateMute(string deviceId, long expectedRevision, Action<CvrUdpClient> write)
    {
        lock (ioGate)
        {
            lock (gate) CheckId(deviceId);
            try
            {
                CvrEndpointGate.UseClient(target, client =>
                {
                    ReadSettings(client);
                    lock (gate) if (revision != expectedRevision) throw new InvalidOperationException("Device changed. Create a new mute plan.");
                    write(client);
                });
                lock (gate) { error = null; UpdateSnapshot(); }
            }
            catch (Exception ex)
            { lock (gate) { forceSettingsRefresh = true; error = ex.Message; UpdateSnapshot(); } throw; }
        }
    }
    private void WriteMuteFlag(CvrUdpClient client, byte function, byte channel, byte segment, byte side, int offset, bool muted, bool route)
    {
        byte[] expected;
        var flag = (byte)(muted ? 0 : 1);
        lock (gate)
        {
            expected = native.ToArray();
            if (expected[offset] == flag) return;
            revision++;
        }
        expected[offset] = flag;
        var body = route ? expected[(offset - 4)..(offset + 1)] : new[] { flag };
        client.Write(function, body, channel, segment, side);
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            ReadSettings(client);
            lock (gate) if (native[offset] == flag) break;
            if (deadline.Elapsed >= TimeSpan.FromSeconds(2)) throw new InvalidOperationException("Mute readback did not confirm the requested state.");
            Thread.Sleep(75);
        }
        lock (gate) if (!native.SequenceEqual(expected)) throw new InvalidOperationException("Unrelated settings changed during muting; current readings were refreshed.");
    }
}

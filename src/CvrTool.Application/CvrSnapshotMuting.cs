// Application / Verified output and routing edits
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: apply captured trim/delay/polarity and routing commands with native readback.
// Units: function 10 output side 1; function 12 output/channel, input/segment side 0.
// State: fresh full settings protect revisions and independently verify each write.
// Scope: unrelated bytes remain unchanged; no automatic retries or route enabling.
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend
{
    public ChannelState Apply(ChannelChange change, long expectedRevision)
    {
        MutateSettings(change.DeviceId, expectedRevision, client =>
        {
            CheckChannel(change.Channel);
            AmplifierService.Merge(settings.Outputs.Single(o => o.Number == change.Channel), change);
            var ch = (byte)(change.Channel - 1); var offset = 36 + (change.Channel - 1) * 515;
            if (change.GainDb is { } gain) WriteSettingsBytes(client, 9, ch, 1, offset + 44, BitConverter.GetBytes((float)gain));
            if (change.DelayMs is { } delay) WriteSettingsBytes(client, 14, ch, 1, offset + 54, BitConverter.GetBytes((float)delay));
            if (change.Inverted is { } inverted) WriteSettingsBytes(client, 18, ch, 1, offset + 58, [(byte)(inverted ? 1 : 0)]);
            if (change.Muted is { } muted) WriteSettingsBytes(client, 10, ch, 1, offset + 48, [(byte)(muted ? 0 : 1)]);
        });
        return GetChannel(change.DeviceId, change.Channel).State;
    }
    public RoutingCellState ApplyRouting(RoutingChange change, long expectedRevision)
    {
        MutateSettings(change.DeviceId, expectedRevision, client =>
        {
            CheckChannel(change.Input); CheckChannel(change.Output);
            var before = settings.Routing!.Single(r => r.Input == change.Input && r.Output == change.Output);
            var after = AmplifierService.MergeRouting(before, change);
            var offset = 36 + (change.Output - 1) * 515 + 24 + (change.Input - 1) * 5;
            var body = BitConverter.GetBytes((float)after.GainDb).Concat(new[] { (byte)(after.Muted ? 0 : 1) }).ToArray();
            WriteSettingsBytes(client, 12, (byte)(change.Output - 1), 0, offset, body, segment:(byte)(change.Input - 1));
        });
        return GetRouting(change.DeviceId).Cells.Single(c => c.Input == change.Input && c.Output == change.Output);
    }
    private void MutateSettings(string deviceId, long expectedRevision, Action<CvrUdpClient> write)
    {
        lock (ioGate)
        {
            lock (gate) CheckId(deviceId);
            try
            {
                CvrEndpointGate.UseClient(target, client =>
                {
                    ReadSettings(client);
                    lock (gate) if (revision != expectedRevision) throw new InvalidOperationException("Device changed. Create a fresh settings plan.");
                    write(client);
                });
                lock (gate) { error = null; UpdateSnapshot(); }
            }
            catch (Exception ex)
            { lock (gate) { forceSettingsRefresh = true; error = ex.Message; UpdateSnapshot(); } throw; }
        }
    }
}

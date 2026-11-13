// Application / Captured output controls
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: release only the observed gate, load-mode and FIR-bypass variants.
// Units: active-low enable flags, signed gate dB and Low-Z/70V mode codes.
// State: each setting is confirmed through fresh native settings, preserving other bytes.
// Scope: reset, bridge, 100V, limiters, FIR coefficients and output EQ remain protected.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend
{
    public OutputDynamicsState ApplyOutputDynamics(OutputDynamicsChange change, long expectedRevision)
    {
        if (change.Bridged is not null || change.Rms is not null || change.Peak is not null || change.LoadMode is > 1)
            throw new NotSupportedException("This output setting has not been physically verified.");
        if (change.LoadMode is null && change.NoiseGateEnabled is null && change.NoiseGateDb is null) throw ReadOnly();
        MutateSettings(change.DeviceId, expectedRevision, client =>
        {
            CheckChannel(change.Channel);
            AmplifierService.MergeDynamics(settings.Dynamics!.Single(d => d.Channel == change.Channel), change);
            var ch = (byte)(change.Channel - 1); var offset = 36 + (change.Channel - 1) * 515;
            if (change.LoadMode is { } mode) WriteSettingsBytes(client, 49, ch, 1, offset + 367, [(byte)mode], messageStatus:0);
            if (change.NoiseGateEnabled is { } gate) WriteSettingsBytes(client, 69, ch, 1, offset + 373, [(byte)(gate ? 0 : 1)]);
            if (change.NoiseGateDb is { } db) WriteSettingsBytes(client, 87, ch, 1, 2232 + change.Channel - 1, [unchecked((byte)(sbyte)db)]);
        });
        return GetOutputDynamics(change.DeviceId, change.Channel).State;
    }
    public FirState ApplyFirEnabled(string deviceId, int channel, bool enabled, long expectedRevision)
    {
        MutateSettings(deviceId, expectedRevision, client =>
        {
            CheckChannel(channel);
            WriteSettingsBytes(client, 44, (byte)(channel - 1), 1, 36 + (channel - 1) * 515 + 368, [(byte)(enabled ? 0 : 1)]);
        });
        return GetFir(deviceId, channel).State;
    }
}

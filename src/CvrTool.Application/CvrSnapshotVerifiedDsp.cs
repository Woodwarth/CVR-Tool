// Application / Captured output controls
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: release only the observed gate, load-mode and FIR-bypass variants.
// Units: active-low enable flags, signed gate dB and Low-Z/70V mode codes.
// State: each setting is confirmed through fresh native settings, preserving other bytes.
// Scope: paired bridge, RMS/peak limiter fields, gate, Low-Z/70V and FIR bypass.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend
{
    public OutputDynamicsState ApplyOutputDynamics(OutputDynamicsChange change, long expectedRevision)
    {
        if (change.LoadMode is > 1)
            throw new NotSupportedException("This output setting has not been physically verified.");
        if (change.Bridged is null && change.Rms is null && change.Peak is null && change.LoadMode is null && change.NoiseGateEnabled is null && change.NoiseGateDb is null) throw ReadOnly();
        MutateSettings(change.DeviceId, expectedRevision, client =>
        {
            CheckChannel(change.Channel);
            var after = AmplifierService.MergeDynamics(settings.Dynamics!.Single(d => d.Channel == change.Channel), change);
            var ch = (byte)(change.Channel - 1); var offset = 36 + (change.Channel - 1) * 515;
            if (change.Bridged.HasValue)
                WriteSettingsBytes(client, 50, (byte)(ch / 2), 1, 34 + ch / 2, [(byte)(after.Bridged ? 0 : 1)]);
            void Parameter(byte function, int position, byte[] value) =>
                WriteSettingsBytes(client, function, ch, 1, offset + position, value);
            if (change.Rms is not null)
            {
                var rms = OutputDynamicsWire.Rms(after.Rms);
                Parameter(39,62,rms[3..7]); Parameter(37,59,rms[..2]); Parameter(38,61,rms[2..3]);
                Parameter(48,67,rms[8..9]); Parameter(46,66,rms[7..8]);
            }
            if (change.Peak is not null)
            {
                var peak = OutputDynamicsWire.Peak(after.Peak);
                Parameter(42,76,peak[4..8]); Parameter(40,72,peak[..2]);
                Parameter(41,74,peak[2..4]); Parameter(47,80,peak[8..9]);
            }
            if (change.LoadMode is { } mode)
            {
                // For a standalone mode request the complete readback is authoritative.
                // A compound edit still needs conflict detection before subsequent writes.
                bool modeOnly = change == new OutputDynamicsChange(change.DeviceId, change.Channel, LoadMode: mode);
                WriteSettingsBytes(client, 49, ch, 1, offset + 367, [(byte)mode], messageStatus: 0,
                    requireUnchangedRemainder: !modeOnly);
            }
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

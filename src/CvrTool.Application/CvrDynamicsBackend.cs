// Application / Physical output dynamics
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: read/write per-output load mode, legacy bridge, noise gate and limiter parameter functions.
// Units: output volts, integer ms, signed dB; all controls use output side 1.
// State: whole-device revisions, immutable limits and independent write readback.
// Scope: explicitly connected peers; no automatic mute, linking or network scan.
using System.Buffers.Binary;
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class CvrLanBackend
{
    public (OutputDynamicsState State, long Revision) GetOutputDynamics(string id, int channel)
    {
        lock (gate)
        {
            CheckId(id);
            var ch = Channel(channel);
            var gateDb = Read(87, ch, side: 1);
            if (gateDb.Length != 1)
                throw new NotSupportedException("Unsupported gate threshold.");
            var state = new OutputDynamicsState(channel, !Flag(Read(50, (byte)(ch / 2), side: 1)), !Flag(Read(69, ch, side: 1)), unchecked((sbyte)gateDb[0]), OutputDynamicsWire.Rms(Read(55, ch, side: 1)), OutputDynamicsWire.Peak(Read(54, ch, side: 1)), OutputDynamicsWire.Mode(Read(49, ch, side: 1)));
            return (state, revision);
        }
    }
    public OutputDynamicsState ApplyOutputDynamics(OutputDynamicsChange change, long revision)
    {
        lock (gate)
        {
            var before = GetOutputDynamics(change.DeviceId, change.Channel).State;
            Expected(revision);
            var after = AmplifierService.MergeDynamics(before, change);
            var ch = Channel(change.Channel);
            byte[] Short(int value)
            {
                var raw = new byte[2];
                BinaryPrimitives.WriteInt16LittleEndian(raw, (short)value);
                return raw;
            }
            if (change.LoadMode is not null)
                Write(49, [(byte)after.LoadMode!.Value], ch, side: 1);
            if (change.Bridged is not null)
                Write(50, [(byte)(after.Bridged ? 0 : 1)], (byte)(ch / 2), side: 1);
            if (change.NoiseGateDb is not null)
                Write(87, [unchecked((byte)(sbyte)after.NoiseGateDb!.Value)], ch, side: 1);
            if (change.NoiseGateEnabled is not null)
                Write(69, [(byte)(after.NoiseGateEnabled ? 0 : 1)], ch, side: 1);
            // Preserve active protection during edits; update bypass after parameter readback.
            if (change.Rms is not null)
            {
                Write(39, FloatBytes(after.Rms.ThresholdVolts), ch, side: 1);
                Write(37, Short(after.Rms.AttackMs), ch, side: 1);
                Write(38, [(byte)after.Rms.ReleaseMultiplier], ch, side: 1);
                Write(48, [(byte)(after.Rms.Automatic ? 1 : 0)], ch, side: 1);
                Write(46, [(byte)(after.Rms.Enabled ? 0 : 1)], ch, side: 1);
            }
            if (change.Peak is not null)
            {
                Write(42, FloatBytes(after.Peak.ThresholdVolts), ch, side: 1);
                Write(40, Short(after.Peak.HoldMs), ch, side: 1);
                Write(41, Short(after.Peak.ReleaseMs), ch, side: 1);
                Write(47, [(byte)(after.Peak.Enabled ? 0 : 1)], ch, side: 1);
            }
            var observed = GetOutputDynamics(change.DeviceId, change.Channel).State;
            var rmsMatches = change.Rms is null || (observed.Rms.Enabled == after.Rms.Enabled && observed.Rms.Automatic == after.Rms.Automatic && observed.Rms.ThresholdVolts == (double)(float)after.Rms.ThresholdVolts && (after.Rms.Automatic || (observed.Rms.AttackMs == after.Rms.AttackMs && observed.Rms.ReleaseMultiplier == after.Rms.ReleaseMultiplier)));
            var peakMatches = change.Peak is null || (observed.Peak.Enabled == after.Peak.Enabled && observed.Peak.ThresholdVolts == (double)(float)after.Peak.ThresholdVolts && observed.Peak.HoldMs == after.Peak.HoldMs && observed.Peak.ReleaseMs == after.Peak.ReleaseMs);
            if (!rmsMatches || !peakMatches || (change.LoadMode is not null && observed.LoadMode != after.LoadMode) || (change.Bridged is not null && observed.Bridged != after.Bridged) || (change.NoiseGateEnabled is not null && observed.NoiseGateEnabled != after.NoiseGateEnabled) || (change.NoiseGateDb is not null && observed.NoiseGateDb != after.NoiseGateDb))
                throw new InvalidOperationException("Dynamics readback differs. Changes may be partial; refresh the device.");
            return observed;
        }
    }
}

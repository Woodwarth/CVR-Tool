// Application / Packed output dynamics
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: translate observed limiter blocks and native channel records.
// Units: LE shorts/floats, volts, ms; active-low bypass/bridge, active-high RMS auto.
// State: encode preserves read-only maximum voltage and unrelated bytes.
// Scope: 13-byte RMS/peak blocks and B118/B119 native sync layouts.
using System.Buffers.Binary;
using CvrTool.Core;

namespace CvrTool.Application;

public static class OutputDynamicsWire
{
    private static bool Active(byte value) => value switch { 0 => true, 1 => false, _ => throw new NotSupportedException("Unsupported dynamics flag.") };
    private static bool AutomaticTiming(byte value) => value switch { 0 => false, 1 => true, _ => throw new NotSupportedException("Unsupported RMS automatic-timing flag.") };
    public static int Mode(byte[] raw)
    {
        if (raw.Length != 1 || raw[0] > 5)
            throw new NotSupportedException("Unsupported output load mode.");
        return raw[0];
    }
    public static RmsLimiterState Rms(byte[] raw)
    {
        if (raw.Length != 13)
            throw new InvalidDataException("Expected 13-byte RMS limiter.");
        return new(Active(raw[7]), ProcessingWire.Float(raw, 3), BinaryPrimitives.ReadInt16LittleEndian(raw), raw[2], AutomaticTiming(raw[8]), ProcessingWire.Float(raw, 9));
    }
    public static PeakLimiterState Peak(byte[] raw)
    {
        if (raw.Length != 13)
            throw new InvalidDataException("Expected 13-byte peak limiter.");
        return new(Active(raw[8]), ProcessingWire.Float(raw, 4), BinaryPrimitives.ReadInt16LittleEndian(raw), BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(2)), ProcessingWire.Float(raw, 9));
    }
    public static byte[] Rms(RmsLimiterState state, byte[]? template = null)
    {
        var raw = template?.ToArray() ?? new byte[13];
        BinaryPrimitives.WriteInt16LittleEndian(raw, (short)state.AttackMs);
        raw[2] = (byte)state.ReleaseMultiplier;
        ProcessingWire.Put(raw, 3, state.ThresholdVolts);
        raw[7] = (byte)(state.Enabled ? 0 : 1);
        raw[8] = (byte)(state.Automatic ? 1 : 0);
        if (template is null)
            ProcessingWire.Put(raw, 9, state.MaximumVolts);
        return raw;
    }
    public static byte[] Peak(PeakLimiterState state, byte[]? template = null)
    {
        var raw = template?.ToArray() ?? new byte[13];
        BinaryPrimitives.WriteInt16LittleEndian(raw, (short)state.HoldMs);
        BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(2), (short)state.ReleaseMs);
        ProcessingWire.Put(raw, 4, state.ThresholdVolts);
        raw[8] = (byte)(state.Enabled ? 0 : 1);
        if (template is null)
            ProcessingWire.Put(raw, 9, state.MaximumVolts);
        return raw;
    }
    public static OutputDynamicsState DecodeNative(byte[] sync, int channel)
    {
        var offset = 36 + (channel - 1) * 515;
        return new(channel, Active(sync[34 + (channel - 1) / 2]), Active(sync[offset + 373]), sync.Length == 2687 ? (int)unchecked((sbyte)sync[2232 + channel - 1]) : null, Rms(sync[(offset + 59)..(offset + 72)]), Peak(sync[(offset + 72)..(offset + 85)]), Mode([sync[offset + 367]]));
    }
    public static void EncodeNative(byte[] sync, OutputDynamicsState state)
    {
        var offset = 36 + (state.Channel - 1) * 515;
        sync[34 + (state.Channel - 1) / 2] = (byte)(state.Bridged ? 0 : 1);
        if (state.LoadMode is { } mode)
            sync[offset + 367] = checked((byte)mode);
        sync[offset + 373] = (byte)(state.NoiseGateEnabled ? 0 : 1);
        if (state.NoiseGateDb is { } gate)
        {
            if (sync.Length == 2687)
                sync[2232 + state.Channel - 1] = unchecked((byte)(sbyte)gate);
        }
        Rms(state.Rms, sync[(offset + 59)..(offset + 72)]).CopyTo(sync, offset + 59);
        Peak(state.Peak, sync[(offset + 72)..(offset + 85)]).CopyTo(sync, offset + 72);
    }
}

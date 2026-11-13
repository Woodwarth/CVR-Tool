// Application / Native preset annotations
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: expose previously opaque labels, input mute and source alignment for inspection.
// Units: 16-byte names, active-low mute, gain dB, delay ms and configured load ohms.
// State: metadata is read-only; original bytes remain the authority on file export.
// Scope: B118/B119 offsets only; no new live commands or source-code assumptions.
using CvrTool.Core;

namespace CvrTool.Application;

public interface INativePresetMetadataSource
{
    NativePresetMetadata? GetNativePresetMetadata(string id);
    string InspectProtocol(string id, int function, int channel, int segment) =>
        throw new NotSupportedException("Protocol inspection requires a discovered amplifier.");
}
internal sealed record NativeMetadataReply(NativePresetMetadata? Metadata);

public sealed record SourceAlignment(double? GainDb, double? DelayMs);
public sealed record InputSourcePriority(byte Primary, byte Secondary, byte BackupFlag, sbyte Threshold);
public sealed record DynamicEqBand(short Attack, short Hold, short Release, byte Knee, double?[] Points);
public sealed record DynamicEqMetadata(byte BypassCode, double?[] FrequenciesHz, DynamicEqBand[] Bands);
public sealed record NativeChannelMetadata(int Channel, string InputName, string SpeakerName,
    bool? InputMuted, byte SourceCode, double? ConfiguredLoadOhms,
    SourceAlignment Analog, SourceAlignment Dante, SourceAlignment Aes, byte? AnalogSourceCode = null,
    InputSourcePriority? Priority = null, DynamicEqMetadata? DynamicEq = null);
public sealed record OutputAllocation(byte ModeCode, byte SensitivityCode, double? LoadOhms, double? Voltage);
public sealed record NativePresetMetadata(string Model, string PresetName, IReadOnlyList<NativeChannelMetadata> Channels, IReadOnlyList<OutputAllocation>? Allocation = null)
{
    public static NativePresetMetadata Decode(byte[] file)
    {
        var layout = NativePresetCodec.Inspect(file);
        double? Number(int offset)
        {
            var value = BitConverter.ToSingle(file, offset);
            return float.IsFinite(value) ? value : null;
        }
        var channels = Enumerable.Range(0, layout.Count).Select(n =>
        {
            var offset = 36 + n * 515;
            SourceAlignment Alignment(int start) => new(Number(offset + start), Number(offset + start + 4));
            bool? muted = file[2192 + n] switch { 0 => true, 1 => false, _ => null };
            int priority = 2200 + n * 4;
            int deq = offset + 410;
            var bands = Enumerable.Range(0, 4).Select(b => new DynamicEqBand(
                BitConverter.ToInt16(file, deq + b * 2), BitConverter.ToInt16(file, deq + 8 + b * 2),
                BitConverter.ToInt16(file, deq + 16 + b * 2), file[deq + 24 + b],
                Enumerable.Range(0, 4).Select(k => Number(deq + 28 + 16 * b + 4 * k)).ToArray())).ToArray();
            return new NativeChannelMetadata(n + 1, ProcessingWire.Text(file[(offset + 378)..(offset + 394)]),
                ProcessingWire.Text(file[(offset + 394)..(offset + 410)]), muted, file[offset + 49], Number(offset + 374),
                Alignment(0), Alignment(8), Alignment(16), file[2196 + n],
                new(file[priority], file[priority + 1], file[priority + 2], unchecked((sbyte)file[priority + 3])),
                new(file[deq + 104], Enumerable.Range(0, 3).Select(k => Number(deq + 92 + 4 * k)).ToArray(), bands));
        }).ToArray();
        return new(layout.Model, ProcessingWire.Text(file[2160..2192]), Array.AsReadOnly(channels));
    }
}

public sealed partial class AmplifierService
{
    public string InspectProtocol(string id, int function, int channel, int segment)
        => ((INativePresetMetadataSource)backend).InspectProtocol(id, function, channel, segment);
    public NativePresetMetadata? GetNativePresetMetadata(string id)
    {
        if (backend is INativePresetMetadataSource source) return source.GetNativePresetMetadata(id);
        backend.GetDevice(id);
        return null;
    }
}

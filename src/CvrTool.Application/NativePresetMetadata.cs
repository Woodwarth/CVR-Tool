// Application / Native preset annotations
// Purpose: expose previously opaque labels, input mute and source alignment for inspection.
// Units: 16-byte names, active-low mute, gain dB, delay ms and configured load ohms.
// State: metadata is read-only; original bytes remain the authority on file export.
// Scope: B118/B119 offsets only; no new live commands or source-code assumptions.
using CvrTool.Core;

namespace CvrTool.Application;

public interface INativePresetMetadataSource
{
    NativePresetMetadata? GetNativePresetMetadata(string id);
}
internal sealed record NativeMetadataReply(NativePresetMetadata? Metadata);

public sealed record SourceAlignment(double? GainDb, double? DelayMs);
public sealed record NativeChannelMetadata(int Channel, string InputName, string SpeakerName,
    bool? InputMuted, byte SourceCode, double? ConfiguredLoadOhms,
    SourceAlignment Analog, SourceAlignment Dante, SourceAlignment Aes);
public sealed record NativePresetMetadata(string Model, string PresetName, IReadOnlyList<NativeChannelMetadata> Channels)
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
            return new NativeChannelMetadata(n + 1, ProcessingWire.Text(file[(offset + 378)..(offset + 394)]),
                ProcessingWire.Text(file[(offset + 394)..(offset + 410)]), muted, file[offset + 49], Number(offset + 374),
                Alignment(0), Alignment(8), Alignment(16));
        }).ToArray();
        return new(layout.Model, ProcessingWire.Text(file[2160..2192]), Array.AsReadOnly(channels));
    }
}

public sealed partial class AmplifierService
{
    public NativePresetMetadata? GetNativePresetMetadata(string id)
    {
        if (backend is INativePresetMetadataSource source) return source.GetNativePresetMetadata(id);
        backend.GetDevice(id);
        return null;
    }
}

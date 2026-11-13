// Checks / Native file and UI-model parity
// Purpose: retain extra annotations and unknown FIR state through application operations.
// Units: synthetic two-/four-channel B118/B119 files and byte-exact exports.
// State: no private presets, identifiers or physical-device assumptions are included.
// Scope: read-only metadata and normal demo processing use the same native template.
using System.Text;
using CvrTool.Application;
using CvrTool.Core;

static class NativeMetadataChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var id in new[] { "demo-1004", "demo-2002" })
        foreach (var legacy in new[] { false, true })
        {
            var service = new AmplifierService(new DemoBackend()); service.AddDemo("DSP-2002");
            check(service.GetNativePresetMetadata(id) is null, "Fresh demos do not invent native source metadata");
            var full = service.ExportNativePreset(id);
            var syncLength = legacy ? 2232 : 2687;
            var sync = full[..syncLength];
            if (legacy) Encoding.ASCII.GetBytes("106118").CopyTo(sync, 9);
            Encoding.ASCII.GetBytes("Preset source").CopyTo(sync, 2160);
            Encoding.ASCII.GetBytes("Input label").CopyTo(sync, 36 + 378);
            Encoding.ASCII.GetBytes("Speaker label").CopyTo(sync, 36 + 394);
            sync[2192] = 0; sync[2193] = 1;
            sync[36 + 49] = 2;
            ProcessingWire.Put(sync, 36 + 374, 8);
            ProcessingWire.Put(sync, 36, -3); ProcessingWire.Put(sync, 36 + 4, 1.25);
            sync[36 + 417] = 0xA5;
            sync[2196] = 2;
            new byte[] { 2, 1, 1, 226 }.CopyTo(sync, 2200);
            ProcessingWire.Put(sync, 36 + 410 + 92, 125);
            ProcessingWire.Put(sync, 36 + 410 + 28 + 16, -6);
            service.LoadPresetFile(id, "source.sd", sync);
            var metadata = service.GetNativePresetMetadata(id)!;
            var channel = metadata.Channels[0];
            check(channel.AnalogSourceCode == 2 && channel.Priority == new InputSourcePriority(2, 1, 1, -30) &&
                channel.DynamicEq!.FrequenciesHz[0] == 125 && channel.DynamicEq.Bands[1].Points[0] == -6,
                "Source priority and per-band dynamic EQ use the native channel offsets");
            check(metadata.PresetName == "Preset source" && metadata.Channels[0] is
                { InputName: "Input label", SpeakerName: "Speaker label", InputMuted: true, SourceCode: 2, ConfiguredLoadOhms: 8 } &&
                metadata.Channels[1].InputMuted == false, "Native labels, separate input mute, source code and configured load are decoded");
            check(metadata.Channels[0].Analog == new SourceAlignment(-3, 1.25), "Source-alignment gain and delay are read independently of DSP trim/delay");
            check(!service.GetFir(id, 1).CoefficientsKnown && service.GetFir(id, 1).Coefficients.Count == 0,
                "Loading settings without a FIR section clears stale demo coefficients to unknown");
            check(service.ExportNativePreset(id).SequenceEqual(sync), "Sync-only file remains byte-identical through the full application load/export path");
            service.StoreDevicePreset(id, 1, "Unknown FIR"); service.SetFir(id, new(1, "Temporary", [1], false)); service.RecallDevicePreset(id, 1);
            check(!service.GetFir(id, 1).CoefficientsKnown && service.ExportNativePreset(id).SequenceEqual(sync),
                "Local preset slots preserve absent FIR data without inventing coefficients");
            var value = service.GetDevice(id).Outputs[0].GainDb == -1 ? -2 : -1;
            service.Apply(service.Plan(new(id, 1, GainDb: value)).Id);
            var expected = sync.ToArray(); ProcessingWire.Put(expected, 36 + 44, value);
            check(service.ExportNativePreset(id).SequenceEqual(expected), "Trim edits preserve all extra names, mute/source data, DEQ and opaque bytes");
            service.SetFir(id, new(1, "Imported", [1], false));
            try { service.ExportNativePreset(id); throw new Exception("Partial unknown FIR set exported"); }
            catch (NotSupportedException) { check(true, "Partially known FIR sets cannot silently become complete files with fabricated filters"); }
            var invalid = sync.ToArray(); invalid[2192] = 2; ProcessingWire.Put(invalid, 36, double.NaN);
            check(NativePresetMetadata.Decode(invalid).Channels[0] is { InputMuted: null, Analog.GainDb: null },
                "Unknown metadata flag/non-finite alignment remains unknown instead of guessed");
        }
    }
}

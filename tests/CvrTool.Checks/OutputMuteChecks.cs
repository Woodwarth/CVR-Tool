// Checks / Active-low output mute
// Purpose: preserve application booleans while mapping CVR output flags correctly.
// Units: native offset 48, byte 0 muted / 1 unmuted; routing is independent.
// State: native edits preserve every unrelated byte and JSON keeps true=muted.
// Scope: synthetic native files only, with user-confirmed wire semantics.
using CvrTool.Application;
using System.Text.Json;
using CvrTool.Core;

static class OutputMuteChecks
{
    public static void Run(Action<bool, string> check)
    {
        var service = new AmplifierService(new DemoBackend());
        const string id = "demo-1004";
        var native = service.ExportNativePreset(id);
        native[36 + 48] = 1;
        native[36 + 515 + 48] = 0;
        var decoded = NativePresetCodec.Decode(native);
        check(!decoded.Outputs[0].Muted && decoded.Outputs[1].Muted, "Native output mute decodes 1=unmuted, 0=muted");
        check(NativePresetCodec.Encode(decoded).SequenceEqual(native), "Active-low native mute round-trips exactly");
        service.LoadPresetFile(id, "mute.sd", native);
        check(!service.GetDevice(id).Outputs[0].Muted && service.GetDevice(id).Outputs[1].Muted, "Native load exposes correct output mute states");
        service.Apply(service.Plan(new(id, 1, Muted: true)).Id);
        var muted = service.ExportNativePreset(id);
        var expected = native.ToArray(); expected[36 + 48] = 0;
        check(muted.SequenceEqual(expected), "Muting a native output changes only its active-low byte");
        var json = JsonSerializer.Deserialize<PresetDocument>(service.ExportPreset(id))!;
        check(json.Outputs[0].Muted, "App JSON retains true=muted independently of wire polarity");
        service.Apply(service.Plan(new(id, 1, Muted: false)).Id);
        check(service.ExportNativePreset(id).SequenceEqual(native), "Unmuting restores original native bytes without changing other outputs");
    }
}

// Checks / Active-low matrix mute
// Purpose: preserve route gain and application booleans across native flag edits.
// Units: five-byte route records, float32 dB followed by 0 muted / 1 enabled.
// State: diagonal fixtures avoid claiming untested physical matrix axis order.
// Scope: local native files only; no physical device writes.
using CvrTool.Application;
using CvrTool.Core;
using System.Text.Json;

static class RoutingMuteChecks
{
    public static void Run(Action<bool, string> check)
    {
        var service = new AmplifierService(new DemoBackend());
        const string id = "demo-1004";
        var native = service.ExportNativePreset(id);
        native[36 + 28] = 1;
        native[36 + 515 + 28 + 5] = 0;
        var decoded = NativePresetCodec.Decode(native);
        var routes = decoded.Routing!;
        check(!routes.Single(c => c.Input == 1 && c.Output == 1).Muted &&
            routes.Single(c => c.Input == 2 && c.Output == 2).Muted,
            "Native route flags decode 1=enabled and 0=muted");
        check(NativePresetCodec.Encode(decoded).SequenceEqual(native), "Native route mute flags round-trip unchanged");
        service.LoadPresetFile(id, "routes.sd", native);
        service.ApplyRouting(service.PlanRouting(new(id, 1, 1, Muted: true)).Id);
        var expected = native.ToArray(); expected[36 + 28] = 0;
        check(service.ExportNativePreset(id).SequenceEqual(expected), "Muting a native route changes only its active-low flag");
        var json = JsonSerializer.Deserialize<PresetDocument>(service.ExportPreset(id))!;
        check(json.Routing!.Single(c => c.Input == 1 && c.Output == 1).Muted, "JSON route mute retains true=muted semantics");
        service.ApplyRouting(service.PlanRouting(new(id, 1, 1, Muted: false)).Id);
        check(service.ExportNativePreset(id).SequenceEqual(native), "Unmuting restores original route bytes");
    }
}

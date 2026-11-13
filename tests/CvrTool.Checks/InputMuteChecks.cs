// Checks / Independent input mute
// Purpose: verify active-low native input mute without changing output/routing flags.
// Units: one-based inputs and global native bytes 2192 onward.
// State: immutable plans, native round-trips and legacy JSON preserve mute state.
// Scope: synthetic two-/four-channel profiles; no device connections.
using CvrTool.Application;
using CvrTool.Core;

static class InputMuteChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach (var id in new[] { "demo-1004", "demo-2002" })
        {
            var service = new AmplifierService(new DemoBackend()); service.AddDemo("DSP-2002");
            var before = service.ExportNativePreset(id);
            check(service.GetInputProcessing(id,1).Muted == false, "Demo input starts independently unmuted");
            service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,Muted:true)).Id);
            var expected = before.ToArray(); expected[2192] = 0;
            check(service.ExportNativePreset(id).SequenceEqual(expected), "Input mute changes only its active-low native flag");
            service.LoadPresetFile(id,"muted.sd",expected);
            check(service.GetInputProcessing(id,1).Muted == true, "Native input mute loads into shared processing state");
            service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,Muted:false)).Id);
            check(service.ExportNativePreset(id).SequenceEqual(before), "Input unmute restores the original preset exactly");
            var invalid = before.ToArray(); invalid[2192]=2;
            service.LoadPresetFile(id,"unknown.sd",invalid);
            check(service.GetInputProcessing(id,1).Muted is null && service.ExportNativePreset(id).SequenceEqual(invalid), "Unknown input mute flag is retained rather than replaced by a demo default");
        }
    }
}

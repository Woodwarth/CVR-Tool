using CvrTool.Application;
using CvrTool.Core;
using System.Text.Json;

var count = 0;
void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
    count++;
}
void Reject<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T) { count++; return; }
    throw new Exception("Expected " + typeof(T).Name);
}
var service = new AmplifierService(new DemoBackend());
Check(service.ListDevices().Single().Id == "demo-1004", "Only DSP-1004 starts by default");
var addedDemo = service.AddDemo("DSP-1502");
Check(addedDemo.Outputs.Count == 2 && service.ListDevices().Count == 2, "Additional demo is an independent session device");
service.RemoveDemo(addedDemo.Id);
Check(service.ListDevices().All(d => d.Id != addedDemo.Id), "Removing a demo leaves the other devices in the session");
service.AddDemo("DSP-2002");
Check(service.GetDevice("demo-2002").Outputs.Count == 2 && !service.GetDevice("demo-2002").Capabilities.VerifiedOnHardware, "DSP-2002 is a two-output unverified demo");
Check(service.GetDevice("demo-1004").Outputs.Count == 4, "Four outputs");
Check(service.GetDevice("demo-2002").Outputs.Count == 2, "Two outputs");
Check(service.ListDevices().All(d => d.IsDemo && d.Capabilities.InputCount == d.Outputs.Count), "Uniform two-/four-channel topology");
Reject<ArgumentOutOfRangeException>(() => service.Plan(new("demo-2002", 3, GainDb: 0)));
Reject<ArgumentOutOfRangeException>(() => service.Plan(new("demo-1004", 1, GainDb: double.NaN)));
Reject<ArgumentOutOfRangeException>(() => service.Plan(new("demo-1004", 1, GainDb: double.PositiveInfinity)));
Reject<ArgumentOutOfRangeException>(() => service.Plan(new("demo-1004", 1, GainDb: 19)));
Reject<ArgumentOutOfRangeException>(() => service.Plan(new("demo-1004", 1, DelayMs: -1)));
Reject<ArgumentException>(() => service.Plan(new("demo-1004", 1)));
var plan = service.Plan(new("demo-1004", 1, GainDb: -6, Muted: false));
Check(service.GetDevice("demo-1004").Outputs[0].GainDb == 0, "Plan must not mutate");
var stale = service.Plan(new("demo-1004", 2, GainDb: -10));
var result = service.Apply(plan.Id);
Check(result.GainDb == -6 && !result.Muted, "Apply");
Check(service.GetDevice("demo-1004").Outputs[1].GainDb == 0, "Other channel untouched");
Check(service.GetDevice("demo-2002").Outputs[0].GainDb == 0, "Other device untouched");
Reject<ArgumentException>(() => service.Apply(plan.Id));
Reject<InvalidOperationException>(() => service.Apply(stale.Id));
var copy = service.GetDevice("demo-1004");
((ChannelState[])copy.Outputs)[0] = copy.Outputs[0] with { GainDb = 18 };
Check(service.GetDevice("demo-1004").Outputs[0].GainDb == -6, "Defensive snapshot");
var preset = JsonSerializer.Deserialize<PresetDocument>(service.ExportPreset("demo-2002"))!;
Check(preset.SchemaVersion == 7 && preset.Outputs.Count == 2 && preset.IsDemo, "Preset export");
var clock = new ManualClock();
var timed = new AmplifierService(new DemoBackend(), clock);
timed.AddDemo("DSP-2002");
var expiring = timed.Plan(new("demo-1004", 1, Muted: false));
clock.Now = clock.Now.AddMinutes(3);
Reject<InvalidOperationException>(() => timed.Apply(expiring.Id));
Check(service.GetControlState("demo-1004") == new DeviceControlState(Wink: false), "Demo controls start unlocked and awake");
Reject<ArgumentException>(() => service.SetControlState("demo-1004"));
Reject<ArgumentException>(() => service.GetControlState("missing"));
var controlStale = service.Plan(new("demo-1004", 1, GainDb: -6));
Check(service.SetControlState("demo-1004", displayLocked: true) == new DeviceControlState(true, false, Wink: false), "Display lock is simulated independently");
Reject<InvalidOperationException>(() => service.Apply(controlStale.Id));
Check(service.SetControlState("demo-1004", standby: true) == new DeviceControlState(true, true, Wink: false), "Standby preserves display lock");
Check(service.GetControlState("demo-2002") == new DeviceControlState(Wink: false), "Demo controls isolate devices");
Check(service.SetControlState("demo-1004", displayLocked: false, standby: false) == new DeviceControlState(Wink: false), "Demo controls can be turned off");
var routes = service.GetRouting("demo-1004");
Check(routes.InputCount == 4 && routes.OutputCount == 4 && routes.Cells.Count == 16, "Four-by-four demo routing");
Check(service.GetRouting("demo-2002").Cells.Count == 4, "Two-by-two demo routing");
Check(service.GetRouting("demo-2002").Cells.Count == 4, "DSP-2002 has the same routing features");
Reject<ArgumentOutOfRangeException>(() => service.PlanRouting(new("demo-1004", 0, 1, GainDb: 0)));
Reject<ArgumentOutOfRangeException>(() => service.PlanRouting(new("demo-1004", 1, 5, GainDb: 0)));
foreach (var value in new[] { double.NaN, double.NegativeInfinity, -80.1, 18.1 })
    Reject<ArgumentOutOfRangeException>(() => service.PlanRouting(new("demo-1004", 1, 1, GainDb: value)));
Reject<ArgumentException>(() => service.PlanRouting(new("demo-1004", 1, 1)));
var routePlan = service.PlanRouting(new("demo-1004", 2, 1, GainDb: -80));
Check(service.GetRouting("demo-1004").Cells.Single(c => c.Input == 2 && c.Output == 1).GainDb == 0, "Routing preview does not write");
Check(service.ApplyRouting(routePlan.Id) is { GainDb: -80, Muted: true }, "Minimum route gain preserves mute");
Reject<ArgumentException>(() => service.ApplyRouting(routePlan.Id));
Check(service.ApplyRouting(service.PlanRouting(new("demo-1004", 2, 1, Muted: false)).Id) is { GainDb: -80, Muted: false }, "Unmute preserves route gain");
Check(service.ApplyRouting(service.PlanRouting(new("demo-1004", 2, 1, GainDb: 18)).Id).GainDb == 18, "Maximum route gain accepted");
Check(service.GetRouting("demo-1004").Cells.Single(c => c.Input == 3 && c.Output == 1) is { GainDb: 0, Muted: true } && service.GetRouting("demo-2002").Cells.Single(c => c.Input == 2 && c.Output == 1).GainDb == 0, "Route changes isolate cells and devices");
var routeCopy = service.GetRouting("demo-1004");
((RoutingCellState[])routeCopy.Cells)[0] = routeCopy.Cells[0] with { GainDb = 17 };
Check(service.GetRouting("demo-1004").Cells[0].GainDb == 0, "Routing snapshots are defensive");
var staleRoute = service.PlanRouting(new("demo-1004", 1, 1, GainDb: -2));
service.Apply(service.Plan(new("demo-1004", 1, GainDb: -6)).Id);
Reject<InvalidOperationException>(() => service.ApplyRouting(staleRoute.Id));
var staleChannel = service.Plan(new("demo-1004", 1, GainDb: -4));
service.ApplyRouting(service.PlanRouting(new("demo-1004", 1, 1, GainDb: -1)).Id);
Reject<InvalidOperationException>(() => service.Apply(staleChannel.Id));
var renameInvalidates = service.PlanRouting(new("demo-1004", 1, 1, Muted: true));
Check(service.RenameDevice("demo-1004", "  Front amplifier  ").Name == "Front amplifier", "Rename trims and stores demo name");
Reject<InvalidOperationException>(() => service.ApplyRouting(renameInvalidates.Id));
foreach (var name in new[] { "", "  ", new string('x', 33), "bad\nname" }) Reject<ArgumentException>(() => service.RenameDevice("demo-1004", name));
Check(AmplifierService.NormalizePhysicalDeviceName("  " + new string('x',31) + "  ").Length==31,"Physical device names allow 31 ASCII bytes plus a wire terminator");
foreach(var name in new[]{new string('x',32),"Ampli\u00e9"}) Reject<ArgumentException>(()=>AmplifierService.NormalizePhysicalDeviceName(name));
Check(AmplifierService.NormalizeDeviceName(new string('x',32)).Length==32,"Local demo name validation retains its 32-character limit");
var renamedPreset = JsonSerializer.Deserialize<PresetDocument>(service.ExportPreset("demo-1004"))!;
Check(renamedPreset.Name == "Front amplifier" && renamedPreset.Routing?.Count == 16, "Preset exports name and routing");
Check(service.GetDevice("demo-2002").Name != "Front amplifier", "Names isolate devices");
var telemetry = service.GetTelemetry("demo-1004");
Check(telemetry.Count == 4 && telemetry.All(t => t is { OutputMeterDb: not null, Voltage: >= 0, Current: >= 0, LoadOhms: > 0, TemperatureC: > 0 }), "Demo channels simulate electrical and thermal measurements");
var meters = new AmplifierService(new DemoBackend());
const string meterId = "demo-1004";
void SetRoute(int input, double gain, bool muted) => meters.ApplyRouting(meters.PlanRouting(new(meterId, input, 1, gain, muted)).Id);
Check(meters.GetTelemetry(meterId).All(t => t.OutputMeterDb == -60 && t.Status?.Clip == false), "Muted demo outputs stay silent");
meters.Apply(meters.Plan(new(meterId, 1, GainDb: -3, Muted: false)).Id);
SetRoute(1, -4, false);
SetRoute(2, -6, false);
var sample = meters.GetTelemetry(meterId);
var expectedMix = 10 * Math.Log10(Math.Pow(10, (sample[0].InputMeterDb!.Value - 4) / 10) + Math.Pow(10, (sample[1].InputMeterDb!.Value - 6) / 10)) - 3;
Check(Math.Abs(sample[0].OutputMeterDb!.Value - expectedMix) < 0.00001 && sample[1].OutputMeterDb == -60, "Matrix gains mix both inputs before channel trim");
SetRoute(1, 18, true);
SetRoute(2, 18, true);
Check(meters.GetTelemetry(meterId)[0].OutputMeterDb == -60, "Muted routes contribute no signal");
SetRoute(1, 0, false);
var rmsDemo = meters.GetOutputDynamics(meterId, 1).Rms with { Enabled = true, ThresholdVolts = 0.01 };
meters.ApplyOutputDynamics(meters.PlanOutputDynamics(new(meterId, 1, Rms: rmsDemo, LoadMode: 1, Bridged: true)).Id);
var bridged = meters.GetTelemetry(meterId);
Check(bridged.Take(2).All(t => t.Status is { HighImpedance: true, Bridged: true, Limiting: true } &&
    t.Voltage == .01 && t.Current == .01 / t.LoadOhms), "Bridged demo outputs follow the leader's limiter and simulated load");
meters.SetControlState(meterId, standby: true);
Check(meters.GetTelemetry(meterId).All(t => t.Voltage == 0 && t.Current == 0 && t.Status is { Standby: true, Limiting: false }),
    "Standby silences every simulated output and clears limiter activity");
var routeExpiring = timed.PlanRouting(new("demo-1004", 1, 1, GainDb: 0));
clock.Now = clock.Now.AddMinutes(2);
Reject<InvalidOperationException>(() => timed.ApplyRouting(routeExpiring.Id));
// Input edits use the same validation and revision rules as outputs and routing.
Check(Math.Abs(AmplifierService.DelayMilliseconds(3.43) - 10) < 1e-10 && AmplifierService.DelayMeters(10) == 3.43, "Delay unit conversion");
var inputBefore = service.GetInputProcessing("demo-2002", 1);
var inputBands = inputBefore.Bands.ToArray();
var inputPlan = service.PlanInputProcessing(new("demo-2002", 1, DelayMs: 10, EqEnabled: false, Bands: inputBands));
inputBands[0] = inputBands[0] with { GainDb = 17 };
Check(service.GetInputProcessing("demo-2002", 1).DelayMs == 0 && inputPlan.After.Bands[0].GainDb == 0, "Input preview freezes caller bands and performs no writes");
service.ApplyInputProcessing(inputPlan.Id);
Check(service.GetInputProcessing("demo-2002", 1) is { DelayMs: 10, EqEnabled: false }, "Input apply updates the selected channel");
Reject<ArgumentException>(() => service.ApplyInputProcessing(inputPlan.Id));
Reject<ArgumentOutOfRangeException>(() => service.PlanInputProcessing(new("demo-2002", 3, DelayMs: 1)));
foreach (var value in new[] { double.NaN, double.PositiveInfinity, -.1, 100.1 }) Reject<ArgumentOutOfRangeException>(() => service.PlanInputProcessing(new("demo-2002", 1, DelayMs: value)));
Reject<ArgumentException>(() => service.PlanInputProcessing(new("demo-2002", 1, Bands: inputBefore.Bands.Take(7).ToArray())));
Reject<ArgumentOutOfRangeException>(() => service.PlanInputProcessing(new("demo-2002", 1, Bands: inputBefore.Bands.Select(b => b with { Q = double.NaN }).ToArray())));
var inputStale = service.PlanInputProcessing(new("demo-2002", 1, DelayMs: 1));
service.ApplyRouting(service.PlanRouting(new("demo-2002", 1, 1, Muted: false)).Id);
Reject<InvalidOperationException>(() => service.ApplyInputProcessing(inputStale.Id));
var inputExpired = timed.PlanInputProcessing(new("demo-2002", 1, DelayMs: 1));
clock.Now = clock.Now.AddMinutes(2);
Reject<InvalidOperationException>(() => timed.ApplyInputProcessing(inputExpired.Id));
Check(JsonSerializer.Deserialize<PresetDocument>(service.ExportPreset("demo-2002"))!.Inputs?.Count == 2, "Preset exports input DSP");
var nativeBytes = new byte[192];
System.Text.Encoding.ASCII.GetBytes("SYNTHETIC-DEMO-DSP-1004").CopyTo(nativeBytes, 0);
System.Text.Encoding.ASCII.GetBytes("FIR==2080").CopyTo(nativeBytes, 176);
var native = NativePresetInspector.Inspect(nativeBytes);
Check(native.ModelHint == "DSP-1004" && native.ByteLength == 192 && !native.CanApply, "Native preset inspection cannot apply parameters");
Check(native.Markers is [{ Offset: 176, Text: "FIR==2080" }], "Native FIR text marker and offset");
Check(native.Sha256 == Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(nativeBytes)), "Native file hash");
Reject<InvalidDataException>(() => NativePresetInspector.Inspect(new byte[31]));
Reject<InvalidDataException>(() => NativePresetInspector.Inspect(new byte[NativePresetInspector.MaximumBytes + 1]));
var unknown = NativePresetInspector.Inspect(new byte[32]);
Check(unknown.ModelHint is null && unknown.Markers.Count == 0 && !unknown.CanApply, "Unknown file does not invent a device model");
nativeBytes[185] = 65;
Check(NativePresetInspector.Inspect(nativeBytes).Markers.Count == 0, "Nonterminated marker rejected");
await using (var stream = new MemoryStream(new byte[32]))
    Check((await NativePresetInspector.InspectAsync(stream)).ByteLength == 32, "Stream inspection");
var firFlat = FirFileAnalyzer.Analyze("1\n0\n0\n0\n");
Check(firFlat.Coefficients.Count == 4 && firFlat.NonZeroCount == 1 && !firFlat.CanApply, "Offline FIR coefficient count and non-applicability");
Check(firFlat.Response.All(p => Math.Abs(p.MagnitudeDb) < 1e-10 && Math.Abs(p.PhaseDegrees) < 1e-10), "Unit impulse has flat zero-dB zero-phase response");
var delayedFir = FirFileAnalyzer.Analyze("0\n1\n");
Check(delayedFir.Response.All(p => Math.Abs(p.MagnitudeDb) < 1e-10), "Pure delay preserves magnitude");
Check(Math.Abs(delayedFir.Response[0].PhaseDegrees + 360 * 20.0 / 48000) < 1e-10, "Pure delay phase matches analytical result");
Check(FirFileAnalyzer.Analyze("1\n1", 40000).Response[^1].MagnitudeDb < -250, "Two-tap sum has Nyquist zero");
Check(FirFileAnalyzer.Analyze("0\n0").Response.All(p => double.IsNegativeInfinity(p.MagnitudeDb) && double.IsNaN(p.PhaseDegrees)), "Null filter phase is explicitly undefined");
Reject<InvalidDataException>(() => FirFileAnalyzer.Analyze("NaN"));
Reject<InvalidDataException>(() => FirFileAnalyzer.Analyze("Infinity"));
Reject<InvalidDataException>(() => FirFileAnalyzer.Analyze("0,5"));
Reject<InvalidDataException>(() => FirFileAnalyzer.Analyze("1 0"));
Reject<InvalidDataException>(() => FirFileAnalyzer.Analyze("\n\n"));
Reject<InvalidDataException>(() => FirFileAnalyzer.Analyze(new string('0', FirFileAnalyzer.MaximumCharacters + 1)));
Reject<InvalidDataException>(() => FirFileAnalyzer.Analyze(string.Join('\n', Enumerable.Repeat("0", FirFileAnalyzer.MaximumCoefficients + 1))));
Reject<ArgumentOutOfRangeException>(() => FirFileAnalyzer.Analyze("1", 0));
var native1502 = new byte[64];
System.Text.Encoding.ASCII.GetBytes("SYNTHETIC-DEMO-DSP-1502").CopyTo(native1502, 0);
Check(NativePresetInspector.Inspect(native1502).ModelHint == "DSP-1502", "Native DSP-1502 model hint");
if (args.Length >= 2 && args[0] == "--preset")
{
    var presetPath = args[1];
    await using var stream = File.OpenRead(presetPath);
    var inspection = await NativePresetInspector.InspectAsync(stream);
    Console.WriteLine($"LOCAL PRESET: {inspection.ModelHint ?? "unknown"}, {inspection.ByteLength} bytes, SHA-256 {inspection.Sha256}");
    foreach (var marker in inspection.Markers)
        Console.WriteLine($"MARKER: {marker.Text} at {marker.Offset}");
}
var firArgument = Array.IndexOf(args, "--fir");
if (firArgument >= 0 && firArgument + 1 < args.Length)
{
    await using var stream = File.OpenRead(args[firArgument + 1]);
    var inspection = await FirFileAnalyzer.AnalyzeAsync(stream);
    Console.WriteLine($"LOCAL FIR: {inspection.Coefficients.Count} coefficients, {inspection.NonZeroCount} nonzero, {inspection.SampleRate} Hz preview");
}
Check(DeviceProfiles.TargetModels.Count == 12 && DeviceProfiles.TargetModels.Select(m => m.Model).Distinct().Count() == 12 && DeviceProfiles.TargetModels.All(m => m.OutputCount is 2 or 4 && m.InputCount == m.OutputCount && m.OutputCount == m.Model[^1] - '0' && !m.VerifiedOnHardware), "All twelve target models derive topology from their last digit without inventing hardware verification");
InputGainChecks.Run(Check);
InputMuteChecks.Run(Check);
OutputMuteChecks.Run(Check);
RoutingMuteChecks.Run(Check);
RoutingAxisChecks.Run(Check);
LoadModeChecks.Run(Check);
DynamicsChecks.Run(Check);
AdvancedChecks.Run(Check);
NativeMetadataChecks.Run(Check);
CrossoverChecks.Run(Check);
await UpdateChecks.RunAsync(Check);
await UpdateChecks.StageNativePackage(args);
Console.WriteLine($"PASS: {count} checks");
sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

// Application / Output processing and preset use cases
// Purpose: share validated output EQ, FIR and sixteen-slot operations with UI/MCP.
// Units: regular EQ units, 48 kHz FIR and one-based preset slots.
// State: plans are immutable, single-use and tied to device revisions.
// Scope: reset/clear execute only when a caller explicitly requests the operation.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class AmplifierService
{
    private IAdvancedAmplifierBackend Advanced => backend as IAdvancedAmplifierBackend ?? throw new NotSupportedException("Advanced processing is not available on this backend.");
    private readonly Dictionary<Guid, InputProcessingPlan> outputPlans = new();
    public InputProcessingState GetOutputProcessing(string id, int channel) => Advanced.GetOutputProcessing(id, channel).State;
    public InputProcessingPlan PlanOutputProcessing(InputProcessingChange change)
    {
        if(change.GainDb is not null || change.Muted is not null)throw new ArgumentException("Output trim uses channel gain, not input processing gain.");
        var (before, revision) = Advanced.GetOutputProcessing(change.DeviceId, change.Channel);
        var after = MergeInput(before, change);
        var frozen = change with
        {
            Bands = change.Bands is null ? null : Array.AsReadOnly(change.Bands.ToArray())
        };
        var plan = new InputProcessingPlan(Guid.NewGuid(), frozen, before with
        {
            Bands = Array.AsReadOnly(before.Bands.ToArray())
        }, after with
        {
            Bands = Array.AsReadOnly(after.Bands.ToArray())
        }, revision, clock.GetUtcNow().AddMinutes(2));
        lock (gate)
        {
            PrunePlans();
            if (plans.Count + routingPlans.Count + inputPlans.Count + outputPlans.Count + dynamicsPlans.Count >= 1000)
                throw new InvalidOperationException("Too many pending plans.");
            outputPlans.Add(plan.Id, plan);
        }
        return plan;
    }
    public InputProcessingState ApplyOutputProcessing(Guid id)
    {
        lock (gate)
        {
            if (!outputPlans.Remove(id, out var plan))
                throw new ArgumentException("Unknown or already used output EQ plan.");
            if (clock.GetUtcNow() >= plan.ExpiresAt)
                throw new InvalidOperationException("Output EQ plan expired.");
            return Advanced.ApplyOutputProcessing(plan.Change, plan.ExpectedRevision);
        }
    }
    public FirState SetFirEnabled(string id, int channel, bool enabled) => Advanced.ApplyFirEnabled(id, channel, enabled, backend.GetChannel(id, channel).Revision);
    public FirState GetFir(string id, int channel) => Advanced.GetFir(id, channel).State;
    public static FirState ValidateFir(FirState state)
    {
        if (!state.CoefficientsKnown || state.SampleRate != 48000 || state.Coefficients.Count is < 1 or > 512 || state.Coefficients.Any(c => !double.IsFinite(c) || Math.Abs(c) > 1e6) || state.Name.Length > 32 || state.Name.Any(char.IsControl))
            throw new ArgumentException("FIR requires 1–512 finite taps at 48 kHz and a name of at most 32 characters.");
        return state with
        {
            Coefficients = Array.AsReadOnly(state.Coefficients.Select(c => (double)(float)c).ToArray())
        };
    }
    public FirState SetFir(string id, FirState state, long? expectedRevision = null)
    {
        state = ValidateFir(state);
        var before = Advanced.GetFir(id, state.Channel);
        return Advanced.ApplyFir(id, state, expectedRevision ?? before.Revision);
    }
    public DevicePresetState GetDevicePresets(string id) => Advanced.GetDevicePresets(id);
    public DeviceOperationResult StoreDevicePreset(string id, int slot, string name) => Advanced.StoreDevicePreset(id, slot, NormalizeDeviceName(name), backend.GetChannel(id, 1).Revision);
    public DeviceOperationResult RecallDevicePreset(string id, int slot) => Advanced.RecallDevicePreset(id, slot, backend.GetChannel(id, 1).Revision);
    public DeviceOperationResult FactoryReset(string id) => Advanced.FactoryReset(id, backend.GetChannel(id, 1).Revision);
    public DeviceOperationResult ClearDevicePresets(string id) => Advanced.ClearDevicePresets(id, backend.GetChannel(id, 1).Revision);
    public DeviceSnapshot LoadPreset(string id, PresetDocument preset) => Advanced.ApplyPreset(id, PresetOperations.Validate(preset, backend.GetDevice(id)), backend.GetChannel(id, 1).Revision);
    public byte[] ExportNativePreset(string id) => NativePresetCodec.Encode(Advanced.CapturePreset(id));
    public DeviceSnapshot LoadPresetFile(string id, string name, byte[] data)
    {
        if (data.Length > NativePresetCodec.MaximumBytes)
            throw new InvalidDataException("Preset exceeds 1 MiB.");
        if (!Path.GetExtension(name).Equals(".sd", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Preset files must use the .sd extension.");
        var preset = NativePresetCodec.Decode(data);
        return LoadPreset(id, preset);
    }
}

public static class PresetOperations
{
    public static PresetDocument Capture(IAmplifierBackend basic, IAdvancedAmplifierBackend advanced, string id, byte[]? native = null)
    {
        var device = basic.GetDevice(id);
        var count = device.Outputs.Count;
        return new(7, device.Capabilities.Model, device.IsDemo, device.Outputs, device.Name, basic.GetRouting(id).Cells,
            Enumerable.Range(1, count).Select(c => basic.GetInputProcessing(id, c).State).ToArray(),
            Enumerable.Range(1, count).Select(c => advanced.GetOutputProcessing(id, c).State).ToArray(),
            Enumerable.Range(1, count).Select(c => advanced.GetFir(id, c).State).ToArray(), native?.ToArray(), Enumerable.Range(1, count).Select(c => advanced.GetOutputDynamics(id, c).State).ToArray());
    }
    public static PresetDocument Copy(PresetDocument preset) => System.Text.Json.JsonSerializer.Deserialize<PresetDocument>(System.Text.Json.JsonSerializer.Serialize(preset))!;
    public static PresetDocument Validate(PresetDocument preset, DeviceSnapshot device)
    {
        var count = device.Outputs.Count;
        if (preset.NativeData is not null)
        {
            var nativePreset=NativePresetCodec.Decode(preset.NativeData);
            var nativeDynamics = nativePreset.Dynamics!;
            preset = preset with
            {
                Inputs=preset.Inputs is null ? nativePreset.Inputs : preset.Inputs.Select(input=>input with {GainDb=input.GainDb ?? nativePreset.Inputs!.Single(n=>n.Channel==input.Channel).GainDb, Muted=input.Muted ?? nativePreset.Inputs!.Single(n=>n.Channel==input.Channel).Muted}).ToArray(),
                Dynamics = preset.Dynamics is null ? nativeDynamics : preset.Dynamics.Select(d => d with { LoadMode = d.LoadMode ?? nativeDynamics.Single(n => n.Channel == d.Channel).LoadMode }).ToArray()
            };
        }
        if (preset.SchemaVersion is < 2 or > 7 || preset.Model != device.Capabilities.Model || preset.Outputs.Count != count || !preset.Outputs.Select(c => c.Number).Order().SequenceEqual(Enumerable.Range(1, count)))
            throw new ArgumentException("Preset model or channel topology does not match the selected device.");
        foreach (var state in preset.Outputs)
            AmplifierService.Merge(state, new(device.Id, state.Number, state.GainDb, state.Muted, state.DelayMs, state.Inverted));
        foreach (var states in new[] { preset.Inputs, preset.OutputProcessing })
        {
            if (states is null)
                continue;
            if (states.Count != count || !states.Select(c => c.Channel).Order().SequenceEqual(Enumerable.Range(1, count)))
                throw new ArgumentException("Preset processing channels are inconsistent.");
            foreach (var state in states)
            {
                if (state.Bands.Any(b => b.FilterType is < 0 or > 127))
                    throw new ArgumentException("Invalid preset EQ type.");
                AmplifierService.MergeInput(state, new(device.Id, state.Channel, state.DelayMs, state.EqEnabled, state.Bands, state.HighPass, state.LowPass,state.GainDb));
            }
        }
        if (preset.Routing is not null)
        {
            if (preset.Routing.Count != count * count || preset.Routing.Select(r => (r.Input, r.Output)).Distinct().Count() != count * count || preset.Routing.Any(r => r.Input < 1 || r.Input > count || r.Output < 1 || r.Output > count))
                throw new ArgumentException("Preset routing topology is inconsistent.");
            foreach (var route in preset.Routing)
                AmplifierService.MergeRouting(route, new(device.Id, route.Input, route.Output, route.GainDb, route.Muted));
        }
        if (preset.Firs is not null)
        {
            if (preset.Firs.Count != count || !preset.Firs.Select(f => f.Channel).Order().SequenceEqual(Enumerable.Range(1, count)))
                throw new ArgumentException("Preset FIR channels are inconsistent.");
            foreach (var fir in preset.Firs)
            {
                if (!fir.CoefficientsKnown && device.IsDemo && fir.Coefficients.Count == 0 && fir.SampleRate == 48000 &&
                    preset.NativeData is { } source && NativePresetCodec.Inspect(source).FirLength == 0) continue;
                AmplifierService.ValidateFir(fir);
            }
        }
        if (preset.Dynamics is not null)
        {
            if (preset.Dynamics.Count != count || !preset.Dynamics.Select(d => d.Channel).Order().SequenceEqual(Enumerable.Range(1, count)))
                throw new ArgumentException("Preset dynamics topology is inconsistent.");
            foreach (var state in preset.Dynamics)
                AmplifierService.ValidateDynamics(state);
            for (var n = 1; n <= count; n += 2)
                if (preset.Dynamics.Single(d => d.Channel == n).Bridged != preset.Dynamics.Single(d => d.Channel == n + 1).Bridged)
                    throw new ArgumentException("Bridge mode must match within each channel pair.");
        }
        if (preset.NativeData is not null)
        {
            var native = NativePresetCodec.Inspect(preset.NativeData);
            if (native.Count != count || native.Model != preset.Model)
                throw new ArgumentException("Native payload does not match the preset.");
        }
        return Copy(preset);
    }
    public static InputProcessingState DefaultProcessing(int channel) => new(channel, 0, true, new[] { 60, 132, 290, 639, 1406, 3092, 6803, 14966 }.Select((f, n) => new EqualizerBand(n + 1, f, 0, 1.41)).ToArray(), new(20), new(20000), Muted: false);
    public static void CheckSlot(int slot)
    {
        if (slot is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(slot), "Preset slot must be 1 through 16.");
    }
}

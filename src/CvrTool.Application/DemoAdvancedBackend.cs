// Application / Local output processing and preset storage
// Purpose: model output EQ, FIR and sixteen independent device slots consistently.
// Units: regular processing units and one-based slot numbers.
// State: all mutations share the device revision and backend gate.
// Scope: local models simulate storage; physical devices use a separate backend.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class DemoBackend : IAdvancedAmplifierBackend
{
    private readonly Dictionary<(string, int), InputProcessingState> outputProcessing = new();
    private readonly Dictionary<(string, int), FirState> firs = new();
    private readonly Dictionary<string, PresetDocument?[]> presetSlots = new();
    private readonly Dictionary<string, string> currentPresetNames = new();
    private readonly Dictionary<string, byte[]> nativeTemplates = new();
    private void Revision(string id, long revision)
    {
        if (Find(id).Revision != revision)
            throw new InvalidOperationException("Device changed. Refresh before applying.");
    }
    private void Changed(string id)
    {
        var entry = Find(id);
        devices[id] = (entry.Device with
        {
            ObservedAt = DateTimeOffset.UtcNow
        }, entry.Revision + 1);
    }
    public (InputProcessingState State, long Revision) GetOutputProcessing(string id, int channel)
    {
        lock (gate)
        {
            var (output, revision) = GetChannel(id, channel);
            var state = outputProcessing.GetValueOrDefault((id, channel)) ?? PresetOperations.DefaultProcessing(channel);
            return (state with
            {
                DelayMs = output.DelayMs,
                Bands = state.Bands.ToArray()
            }, revision);
        }
    }
    public InputProcessingState ApplyOutputProcessing(InputProcessingChange change, long revision)
    {
        lock (gate)
        {
            var before = GetOutputProcessing(change.DeviceId, change.Channel).State;
            Revision(change.DeviceId, revision);
            var after = AmplifierService.MergeInput(before, change);
            outputProcessing[(change.DeviceId, change.Channel)] = after;
            var entry = Find(change.DeviceId);
            devices[change.DeviceId] = (entry.Device with
            {
                Outputs = entry.Device.Outputs.Select(c => c.Number == change.Channel ? c with { DelayMs = after.DelayMs } : c).ToArray()
            }, revision);
            Changed(change.DeviceId);
            return after with
            {
                Bands = after.Bands.ToArray()
            };
        }
    }
    public (FirState State, long Revision) GetFir(string id, int channel)
    {
        lock (gate)
        {
            var (_, revision) = GetChannel(id, channel);
            var state = firs.GetValueOrDefault((id, channel)) ?? new(channel, "", new double[] { 1 });
            return (state with
            {
                Coefficients = state.Coefficients.ToArray()
            }, revision);
        }
    }
    public FirState ApplyFir(string id, FirState state, long revision)
    {
        lock (gate)
        {
            GetChannel(id, state.Channel);
            Revision(id, revision);
            state = AmplifierService.ValidateFir(state);
            firs[(id, state.Channel)] = state;
            Changed(id);
            return state with
            {
                Coefficients = state.Coefficients.ToArray()
            };
        }
    }
    public PresetDocument CapturePreset(string id)
    {
        lock (gate)
            return PresetOperations.Capture(this, this, id, nativeTemplates.GetValueOrDefault(id));
    }
    public DeviceSnapshot ApplyPreset(string id, PresetDocument preset, long revision)
    {
        lock (gate)
        {
            Revision(id, revision);
            preset = PresetOperations.Validate(preset, GetDevice(id));
            var entry = Find(id);
            devices[id] = (entry.Device with
            {
                Outputs = preset.Outputs.ToArray()
            }, revision);
            if (preset.Inputs is not null)
                inputs[id] = preset.Inputs.Select(input=>input with {GainDb=input.GainDb ?? GetInputProcessing(id,input.Channel).State.GainDb}).ToArray();
            if (preset.Routing is not null)
                routing[id] = preset.Routing.ToArray();
            foreach (var state in preset.OutputProcessing ?? [])
                outputProcessing[(id, state.Channel)] = state;
            foreach (var state in preset.Firs ?? [])
                firs[(id, state.Channel)] = state;
            foreach (var state in preset.Dynamics ?? [])
                dynamics[(id, state.Channel)] = state with
                {
                    LoadMode = state.LoadMode ?? GetOutputDynamics(id, state.Channel).State.LoadMode
                };
            if (preset.NativeData is not null)
                nativeTemplates[id] = preset.NativeData.ToArray();
            Changed(id);
            return GetDevice(id);
        }
    }
    private PresetDocument?[] Slots(string id)
    {
        Find(id);
        if (!presetSlots.TryGetValue(id, out var slots))
            presetSlots[id] = slots = new PresetDocument?[16];
        return slots;
    }
    public DevicePresetState GetDevicePresets(string id)
    {
        lock (gate)
            return new(Slots(id).Select((p, n) => new DevicePresetSlot(n + 1, p?.Name ?? "Empty", p is not null)).ToArray(), currentPresetNames.GetValueOrDefault(id));
    }
    public DeviceOperationResult StoreDevicePreset(string id, int slot, string name, long revision)
    {
        lock (gate)
        {
            PresetOperations.CheckSlot(slot);
            Revision(id, revision);
            name = AmplifierService.NormalizeDeviceName(name);
            Slots(id)[slot - 1] = PresetOperations.Copy(CapturePreset(id) with
            {
                Name = name
            });
            currentPresetNames[id] = name;
            Changed(id);
            return new("Store", true, $"Preset stored in slot {slot}.");
        }
    }
    public DeviceOperationResult RecallDevicePreset(string id, int slot, long revision)
    {
        lock (gate)
        {
            PresetOperations.CheckSlot(slot);
            Revision(id, revision);
            var preset = Slots(id)[slot - 1] ?? throw new InvalidOperationException("This preset slot is empty.");
            ApplyPreset(id, preset, revision);
            currentPresetNames[id] = preset.Name ?? "";
            return new("Recall", true, $"Preset {slot} loaded.");
        }
    }
    public DeviceOperationResult ClearDevicePresets(string id, long revision)
    {
        lock (gate)
        {
            Revision(id, revision);
            presetSlots[id] = new PresetDocument?[16];
            currentPresetNames.Remove(id);
            Changed(id);
            return new("Clear", true, "All sixteen preset slots cleared.");
        }
    }
    public DeviceOperationResult FactoryReset(string id, long revision)
    {
        lock (gate)
        {
            Revision(id, revision);
            var entry = Find(id);
            devices[id] = (Create(id, entry.Device.Capabilities) with
            {
                Name = entry.Device.Name
            }, revision + 1);
            routing[id] = CreateRouting(entry.Device.Outputs.Count);
            foreach (var key in dynamics.Keys.Where(k => k.Item1 == id).ToArray())
                dynamics.Remove(key);
            inputs.Remove(id);
            controls.Remove(id);
            nativeTemplates.Remove(id);
            presetSlots.Remove(id);
            currentPresetNames.Remove(id);
            foreach (var key in outputProcessing.Keys.Where(k => k.Item1 == id).ToArray())
                outputProcessing.Remove(key);
            foreach (var key in firs.Keys.Where(k => k.Item1 == id).ToArray())
                firs.Remove(key);
            return new("Factory reset", true, "Factory settings restored; local device presets cleared.");
        }
    }
}

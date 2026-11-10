// Application / Input state backend
// Purpose: store delay and EQ in the shared local session.
// Units: delay ms, frequency Hz, gain dB, Q.
// State: defensive snapshots and whole-device revisions.
// Scope: identical DSP controls for every two-/four-channel profile.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class DemoBackend
{
    private readonly Dictionary<string, InputProcessingState[]> inputs = new();
    private InputProcessingState[] FindInputs(string id)
    {
        var device = Find(id).Device;
        if (!inputs.TryGetValue(id, out var states))
        {
            var frequencies = new[] { 60, 132, 290, 639, 1406, 3092, 6803, 14966 };
            states = Enumerable.Range(1, device.Capabilities.OutputCount).Select(channel => new InputProcessingState(channel, 0, true,
                frequencies.Select((frequency, band) => new EqualizerBand(band + 1, frequency, 0, 1.41)).ToArray(), new CrossoverFilter(20), new CrossoverFilter(20000))).ToArray();
            inputs.Add(id, states);
        }
        return states;
    }
    public (InputProcessingState State, long Revision) GetInputProcessing(string deviceId, int channel)
    {
        lock (gate)
        {
            var state = FindInputs(deviceId).SingleOrDefault(s => s.Channel == channel) ?? throw new ArgumentOutOfRangeException(nameof(channel));
            return (state with
            {
                Bands = state.Bands.ToArray()
            }, Find(deviceId).Revision);
        }
    }
    public InputProcessingState ApplyInputProcessing(InputProcessingChange change, long expectedRevision)
    {
        lock (gate)
        {
            var (before, revision) = GetInputProcessing(change.DeviceId, change.Channel);
            if (revision != expectedRevision)
                throw new InvalidOperationException("Device changed. Create a new input plan.");
            var after = AmplifierService.MergeInput(before, change);
            inputs[change.DeviceId] = FindInputs(change.DeviceId).Select(s => s.Channel == change.Channel ? after : s).ToArray();
            var entry = Find(change.DeviceId);
            devices[change.DeviceId] = (entry.Device with
            {
                ObservedAt = DateTimeOffset.UtcNow
            }, revision + 1);
            return after with
            {
                Bands = after.Bands.ToArray()
            };
        }
    }
}

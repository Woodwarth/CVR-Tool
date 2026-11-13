// Application / Local dynamics model
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: store per-output load mode, gate/limiters and legacy paired bridge.
// Units: output volts, ms, dB and RMS release multiplier.
// State: mutations and bulk restore share the backend revision and gate.
// Scope: local reference values do not establish physical amplifier limits.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class DemoBackend
{
    private readonly Dictionary<(string, int), OutputDynamicsState> dynamics = new();
    private OutputDynamicsState DefaultDynamics(string id, int channel)
    {
        var rms = id == "demo-1502" ? 113.1f : 93.8f;
        var peak = id == "demo-1502" ? 159.9f : 132.6f;
        return new(channel, false, false, -100, new(true, rms, 25, 16, false, rms), new(true, peak, 50, 300, peak), 0);
    }
    public (OutputDynamicsState State, long Revision) GetOutputDynamics(string id, int channel)
    {
        lock (gate)
        {
            var (_, revision) = GetChannel(id, channel);
            return (dynamics.GetValueOrDefault((id, channel)) ?? DefaultDynamics(id, channel), revision);
        }
    }
    public OutputDynamicsState ApplyOutputDynamics(OutputDynamicsChange change, long revision)
    {
        lock (gate)
        {
            var before = GetOutputDynamics(change.DeviceId, change.Channel).State;
            Revision(change.DeviceId, revision);
            var state = AmplifierService.MergeDynamics(before, change);
            dynamics[(change.DeviceId, change.Channel)] = state;
            if (change.Bridged is not null)
            {
                var partner = (change.Channel - 1) ^ 1;
                partner++;
                var other = GetOutputDynamics(change.DeviceId, partner).State;
                dynamics[(change.DeviceId, partner)] = other with
                {
                    Bridged = state.Bridged
                };
            }
            Changed(change.DeviceId);
            return state;
        }
    }
}

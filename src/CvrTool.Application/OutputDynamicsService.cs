// Application / Output dynamics use cases
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: validate edits against device voltage limits and shared revisions.
// Units: output volts, ms, integer dB and RMS release/attack multiplier.
// State: plans are bounded, single-use and expire after two minutes.
// Scope: load mode is per output; legacy bridge remains paired; no inferred telemetry.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class AmplifierService
{
    private readonly Dictionary<Guid, OutputDynamicsPlan> dynamicsPlans = new();
    public OutputDynamicsState GetOutputDynamics(string id, int channel) => Advanced.GetOutputDynamics(id, channel).State;
    public OutputDynamicsPlan PlanOutputDynamics(OutputDynamicsChange change)
    {
        var (before, revision) = Advanced.GetOutputDynamics(change.DeviceId, change.Channel);
        var plan = new OutputDynamicsPlan(Guid.NewGuid(), change, before, MergeDynamics(before, change), revision, clock.GetUtcNow().AddMinutes(2));
        lock (gate)
        {
            PrunePlans();
            if (plans.Count + routingPlans.Count + inputPlans.Count + outputPlans.Count + dynamicsPlans.Count >= 1000)
                throw new InvalidOperationException("Too many pending plans.");
            dynamicsPlans.Add(plan.Id, plan);
        }
        return plan;
    }
    public OutputDynamicsState ApplyOutputDynamics(Guid id)
    {
        lock (gate)
        {
            if (!dynamicsPlans.Remove(id, out var plan))
                throw new ArgumentException("Unknown or already used dynamics plan.");
            if (clock.GetUtcNow() >= plan.ExpiresAt)
                throw new InvalidOperationException("Dynamics plan expired.");
            return Advanced.ApplyOutputDynamics(plan.Change, plan.ExpectedRevision);
        }
    }
    public static OutputDynamicsState MergeDynamics(OutputDynamicsState before, OutputDynamicsChange change)
    {
        if (change.NoiseGateDb is not null && before.NoiseGateDb is null)
            throw new NotSupportedException("This layout does not expose a noise-gate threshold.");
        if (change.LoadMode is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(change), "Choose Low-Z, 70V or 100V.");
        var state = before with
        {
            Bridged = change.Bridged ?? before.Bridged,
            LoadMode = change.LoadMode ?? before.LoadMode,
            NoiseGateEnabled = change.NoiseGateEnabled ?? before.NoiseGateEnabled,
            NoiseGateDb = change.NoiseGateDb ?? before.NoiseGateDb,
            Rms = change.Rms ?? before.Rms,
            Peak = change.Peak ?? before.Peak
        };
        if (state.Rms.MaximumVolts != before.Rms.MaximumVolts || state.Peak.MaximumVolts != before.Peak.MaximumVolts)
            throw new ArgumentException("Limiter maximum voltage is read-only.");
        ValidateDynamics(state);
        return state;
    }
    public static void ValidateDynamics(OutputDynamicsState state)
    {
        if (state.LoadMode is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(state), "Unsupported output load mode.");
        var r = state.Rms;
        var p = state.Peak;
        if (state.NoiseGateDb is < -100 or > 0 || r.AttackMs is < 1 or > 2000 || r.ReleaseMultiplier is < 2 or > 32 || p.HoldMs is < 0 or > 2000 || p.ReleaseMs is < 20 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(state), "Invalid gate or limiter timing.");
        foreach (var (threshold, maximum) in new[] { (r.ThresholdVolts, r.MaximumVolts), (p.ThresholdVolts, p.MaximumVolts) })
            if (!double.IsFinite(threshold) || !double.IsFinite(maximum) || maximum <= 0 || threshold < 0 || threshold > maximum)
                throw new ArgumentOutOfRangeException(nameof(state), "Limiter threshold must be finite and within the device voltage limit.");
    }
}

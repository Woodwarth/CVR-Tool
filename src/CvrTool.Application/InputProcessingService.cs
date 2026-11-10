// Application / Input DSP
// Purpose: validate delay/EQ edits through the same plan lifecycle as channel edits.
// Units: delay is stored in ms; metres assume 343 m/s for display.
// State: plans are single-use and bound to the whole device revision.
// Scope: no separate behavior branches for two-channel models.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class AmplifierService
{
    private readonly Dictionary<Guid, InputProcessingPlan> inputPlans = new();
    public const double SpeedOfSoundMetersPerSecond = 343;
    public static double DelayMeters(double milliseconds) => milliseconds * SpeedOfSoundMetersPerSecond / 1000;
    public static double DelayMilliseconds(double meters) => meters * 1000 / SpeedOfSoundMetersPerSecond;
    public InputProcessingState GetInputProcessing(string id, int channel) => backend.GetInputProcessing(id, channel).State;

    public static InputProcessingState MergeInput(InputProcessingState before, InputProcessingChange change)
    {
        if (change.DelayMs is { } delay && (!double.IsFinite(delay) || delay is < 0 or > 100))
            throw new ArgumentOutOfRangeException(nameof(change), "Input delay must be between 0 and 100 ms.");
        if (change.DelayMs is null && change.EqEnabled is null && change.Bands is null)
            throw new ArgumentException("Supply delay, EQ enable state or EQ bands.");
        var bands = change.Bands ?? before.Bands;
        if (bands.Count != 8 || !bands.Select(b => b.Number).Order().SequenceEqual(Enumerable.Range(1, 8)))
            throw new ArgumentException("Input EQ requires exactly eight uniquely numbered bands.");
        foreach (var band in bands)
        {
            if (!double.IsFinite(band.FrequencyHz) || band.FrequencyHz is < 20 or > 20000 ||
                !double.IsFinite(band.GainDb) || band.GainDb is < -18 or > 18 ||
                !double.IsFinite(band.Q) || band.Q is < .1 or > 20)
                throw new ArgumentOutOfRangeException(nameof(change), "EQ ranges: 20–20,000 Hz, −18 to +18 dB, Q 0.1–20.");
        }
        return before with
        {
            DelayMs = change.DelayMs ?? before.DelayMs,
            EqEnabled = change.EqEnabled ?? before.EqEnabled,
            Bands = bands.OrderBy(b => b.Number).ToArray()
        };
    }

    public InputProcessingPlan PlanInputProcessing(InputProcessingChange change)
    {
        var (before, revision) = backend.GetInputProcessing(change.DeviceId, change.Channel);
        var after = MergeInput(before, change);
        // Freeze caller-owned lists so later edits cannot change a previewed plan.
        var frozen = change with
        {
            Bands = change.Bands is null ? null : Array.AsReadOnly(change.Bands.ToArray())
        };
        before = before with
        {
            Bands = Array.AsReadOnly(before.Bands.ToArray())
        };
        after = after with
        {
            Bands = Array.AsReadOnly(after.Bands.ToArray())
        };
        var plan = new InputProcessingPlan(Guid.NewGuid(), frozen, before, after, revision, clock.GetUtcNow().AddMinutes(2));
        lock (gate)
        {
            PrunePlans();
            if (plans.Count + routingPlans.Count + inputPlans.Count >= 1000)
                throw new InvalidOperationException("Too many pending plans.");
            inputPlans.Add(plan.Id, plan);
        }
        return plan;
    }
    public InputProcessingState ApplyInputProcessing(Guid id)
    {
        lock (gate)
        {
            if (!inputPlans.Remove(id, out var plan))
                throw new ArgumentException("Unknown or already used input plan.");
            if (clock.GetUtcNow() >= plan.ExpiresAt)
                throw new InvalidOperationException("Input plan expired.");
            return backend.ApplyInputProcessing(plan.Change, plan.ExpectedRevision);
        }
    }
    private void PrunePlans()
    {
        var now = clock.GetUtcNow();
        foreach (var id in plans.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray())
            plans.Remove(id);
        foreach (var id in routingPlans.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray())
            routingPlans.Remove(id);
        foreach (var id in inputPlans.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray())
            inputPlans.Remove(id);
    }
}

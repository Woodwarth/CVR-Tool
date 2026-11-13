// Application / Routing — validate matrix changes against one device revision.
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class AmplifierService
{
    private readonly Dictionary<Guid, RoutingPlan> routingPlans = new();
    public RoutingSnapshot GetRouting(string id) => backend.GetRouting(id);
    public static RoutingCellState MergeRouting(RoutingCellState before, RoutingChange change)
    {
        if (change.GainDb is { } gain && (!double.IsFinite(gain) || gain is < -80 or > 18))
            throw new ArgumentOutOfRangeException(nameof(change), "Routing gain must be between -80 and +18 dB.");
        if (change.GainDb is null && change.Muted is null)
            throw new ArgumentException("Supply routing gain or mute.");
        return before with
        {
            GainDb = change.GainDb ?? before.GainDb,
            Muted = change.Muted ?? before.Muted
        };
    }

    /// <summary>Preview one route while preserving independent gain and mute values.</summary>
    public RoutingPlan PlanRouting(RoutingChange change)
    {
        var snapshot = backend.GetRouting(change.DeviceId);
        var before = snapshot.Cells.SingleOrDefault(c => c.Input == change.Input && c.Output == change.Output) ?? throw new ArgumentOutOfRangeException(nameof(change), "Routing cell does not exist.");
        var plan = new RoutingPlan(Guid.NewGuid(), change, before, MergeRouting(before, change), snapshot.Revision, clock.GetUtcNow().AddMinutes(2));
        lock (gate)
        {
            PrunePlans();
            if (plans.Count + routingPlans.Count + inputPlans.Count + outputPlans.Count + dynamicsPlans.Count >= 1000)
                throw new InvalidOperationException("Too many pending plans.");
            routingPlans.Add(plan.Id, plan);
        }

        return plan;
    }

    /// <summary>Apply a single-use route plan under the same revision rules as channel changes.</summary>
    public RoutingCellState ApplyRouting(Guid id)
    {
        lock (gate)
        {
            if (!routingPlans.Remove(id, out var plan))
                throw new ArgumentException("Unknown or already used routing plan.");
            if (clock.GetUtcNow() >= plan.ExpiresAt)
                throw new InvalidOperationException("Routing plan expired.");
            return backend.ApplyRouting(plan.Change, plan.ExpectedRevision);
        }
    }
}

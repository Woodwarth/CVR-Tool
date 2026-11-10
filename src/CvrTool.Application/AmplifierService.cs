// Application / Channels — shared parameter validation and single-use change plans.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class AmplifierService(IAmplifierBackend backend, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<Guid, ChangePlan> plans = new();
    public IReadOnlyList<DeviceSnapshot> ListDevices() => backend.ListDevices();
    public DeviceSnapshot GetDevice(string id) => backend.GetDevice(id);
    // These ranges are simulation constraints, not asserted hardware capabilities.
    /// <summary>Validate demo ranges and merge only supplied parameters; the original snapshot stays unchanged.</summary>
    public static ChannelState Merge(ChannelState before, ChannelChange change)
    {
        if (change.GainDb is { } gain && (!double.IsFinite(gain) || gain < -80 || gain > 18))
            throw new ArgumentOutOfRangeException(nameof(change), "Demo gain range: -80 to +18 dB.");
        if (change.DelayMs is { } delay && (!double.IsFinite(delay) || delay < 0 || delay > 100))
            throw new ArgumentOutOfRangeException(nameof(change), "Demo delay range: 0 to 100 ms.");
        if (change.GainDb is null && change.Muted is null && change.DelayMs is null && change.Inverted is null)
            throw new ArgumentException("At least one parameter must be supplied.", nameof(change));
        return before with
        {
            GainDb = change.GainDb ?? before.GainDb,
            Muted = change.Muted ?? before.Muted,
            DelayMs = change.DelayMs ?? before.DelayMs,
            Inverted = change.Inverted ?? before.Inverted
        };
    }

    /// <summary>Create a read-only preview tied to a device revision, valid for two minutes.</summary>
    public ChangePlan Plan(ChannelChange change)
    {
        var (state, revision) = backend.GetChannel(change.DeviceId, change.Channel);
        var plan = new ChangePlan(Guid.NewGuid(), change, state, Merge(state, change), revision, clock.GetUtcNow().AddMinutes(2));
        lock (gate)
        {
            PrunePlans();
            if (plans.Count + routingPlans.Count + inputPlans.Count >= 1000)
                throw new InvalidOperationException("Too many pending plans.");
            plans.Add(plan.Id, plan);
        }

        return plan;
    }

    /// <summary>Consume a plan once; reject expired plans and concurrent device changes.</summary>
    public ChannelState Apply(Guid planId)
    {
        lock (gate)
        {
            if (!plans.Remove(planId, out var plan))
                throw new ArgumentException("Unknown or already used plan.", nameof(planId));
            if (clock.GetUtcNow() >= plan.ExpiresAt)
                throw new InvalidOperationException("Plan expired. Create a new plan.");
            return backend.Apply(plan.Change, plan.ExpectedRevision);
        }
    }
}

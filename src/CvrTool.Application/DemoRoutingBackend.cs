// Application / Demo routing — in-memory matrix changes with revision checks.
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class DemoBackend
{
    public RoutingSnapshot GetRouting(string deviceId)
    {
        lock (gate)
        {
            var entry = Find(deviceId);
            if (!routing.TryGetValue(deviceId, out var cells))
                throw new NotSupportedException("Input topology for this demo is not configured.");
            return new(deviceId, cells.Max(c => c.Input), entry.Device.Outputs.Count, cells.ToArray(), entry.Revision);
        }
    }

    public RoutingCellState ApplyRouting(RoutingChange change, long expectedRevision)
    {
        lock (gate)
        {
            var snapshot = GetRouting(change.DeviceId);
            if (snapshot.Revision != expectedRevision)
                throw new InvalidOperationException("Device changed. Create a new routing plan.");
            var before = snapshot.Cells.SingleOrDefault(c => c.Input == change.Input && c.Output == change.Output) ?? throw new ArgumentOutOfRangeException(nameof(change));
            var after = AmplifierService.MergeRouting(before, change);
            routing[change.DeviceId] = snapshot.Cells.Select(c => c.Input == change.Input && c.Output == change.Output ? after : c).ToArray();
            var entry = Find(change.DeviceId);
            devices[change.DeviceId] = (entry.Device with
            {
                ObservedAt = DateTimeOffset.UtcNow
            }, entry.Revision + 1);
            return after;
        }
    }
}

// Core / Routing — matrix cells, independent gain/mute and change plans.
// Purpose: define immutable application data and backend boundaries.
// Input: explicit channel numbers and parameter values.
// State: snapshots describe local or observed device settings.
// Scope: nullable readings remain unknown until measured.
namespace CvrTool.Core;

public sealed record RoutingCellState(int Input, int Output, double GainDb, bool Muted);
public sealed record RoutingSnapshot(
    string DeviceId,
    int InputCount,
    int OutputCount,
    IReadOnlyList<RoutingCellState> Cells,
    long Revision);
public sealed record RoutingChange(
    string DeviceId,
    int Input,
    int Output,
    double? GainDb = null,
    bool? Muted = null);
public sealed record RoutingPlan(
    Guid Id,
    RoutingChange Change,
    RoutingCellState Before,
    RoutingCellState After,
    long ExpectedRevision,
    DateTimeOffset ExpiresAt);

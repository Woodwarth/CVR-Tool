// Core / Presets — versioned, portable demo-state export.
// Module: Defines device models, channel state and the contracts shared by all backends.
// Purpose: define immutable application data and backend boundaries.
// Input: explicit channel numbers and parameter values.
// State: snapshots describe local or observed device settings.
// Scope: nullable readings remain unknown until measured.
namespace CvrTool.Core;

public sealed record PresetDocument(
    int SchemaVersion,
    string Model,
    bool IsDemo,
    IReadOnlyList<ChannelState> Outputs,
    string? Name = null,
    IReadOnlyList<RoutingCellState>? Routing = null,
    IReadOnlyList<InputProcessingState>? Inputs = null,
    IReadOnlyList<InputProcessingState>? OutputProcessing = null,
    IReadOnlyList<FirState>? Firs = null,
    byte[]? NativeData = null,
    IReadOnlyList<OutputDynamicsState>? Dynamics = null);

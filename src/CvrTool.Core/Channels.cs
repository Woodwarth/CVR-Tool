// Core / Channels — output settings and revision-bound change plans.
// Module: Defines device models, channel state and the contracts shared by all backends.
// Purpose: define immutable application data and backend boundaries.
// Input: explicit channel numbers and parameter values.
// State: snapshots describe local or observed device settings.
// Scope: nullable readings remain unknown until measured.
namespace CvrTool.Core;

public sealed record ChannelState(
    int Number,
    string Name,
    double GainDb,
    bool Muted,
    double DelayMs,
    bool Inverted);
public sealed record ChannelChange(
    string DeviceId,
    int Channel,
    double? GainDb = null,
    bool? Muted = null,
    double? DelayMs = null,
    bool? Inverted = null);
public sealed record ChangePlan(
    Guid Id,
    ChannelChange Change,
    ChannelState Before,
    ChannelState After,
    long ExpectedRevision,
    DateTimeOffset ExpiresAt);

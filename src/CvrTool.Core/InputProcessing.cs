// Core / Input DSP
// Purpose: describe independent input gain, delay and eight peak EQ bands.
// Units: milliseconds, hertz, decibels and dimensionless Q.
// State: immutable snapshots shared by UI, MCP and backends.
// Scope: channel counts vary; processing behavior is identical.
namespace CvrTool.Core;

public sealed record EqualizerBand(int Number, double FrequencyHz, double GainDb, double Q, bool Enabled = true, int FilterType = 0);
public sealed record InputProcessingState(int Channel, double DelayMs, bool EqEnabled, IReadOnlyList<EqualizerBand> Bands, CrossoverFilter? HighPass = null, CrossoverFilter? LowPass = null, double? GainDb = null, bool? Muted = null);
public sealed record InputProcessingChange(string DeviceId, int Channel, double? DelayMs = null, bool? EqEnabled = null, IReadOnlyList<EqualizerBand>? Bands = null, CrossoverFilter? HighPass = null, CrossoverFilter? LowPass = null, double? GainDb = null, bool? Muted = null);
public sealed record InputProcessingPlan(Guid Id, InputProcessingChange Change, InputProcessingState Before, InputProcessingState After, long ExpectedRevision, DateTimeOffset ExpiresAt);

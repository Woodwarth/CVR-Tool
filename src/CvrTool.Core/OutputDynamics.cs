// Core / Output dynamics
// Purpose: describe paired stereo/bridge, per-output noise gate and limiters.
// Units: volts at the output, integer ms, RMS release as an attack multiplier.
// State: maximum voltages are read-only device limits; null gate threshold is unknown.
// Scope: immutable snapshots and revision-bound changes shared by UI and MCP.
namespace CvrTool.Core;

public sealed record RmsLimiterState(bool Enabled, double ThresholdVolts, int AttackMs, int ReleaseMultiplier, bool Automatic, double MaximumVolts);
public sealed record PeakLimiterState(bool Enabled, double ThresholdVolts, int HoldMs, int ReleaseMs, double MaximumVolts);
public sealed record OutputDynamicsState(int Channel, bool Bridged, bool NoiseGateEnabled, int? NoiseGateDb, RmsLimiterState Rms, PeakLimiterState Peak);
public sealed record OutputDynamicsChange(string DeviceId, int Channel, bool? Bridged = null, bool? NoiseGateEnabled = null, int? NoiseGateDb = null, RmsLimiterState? Rms = null, PeakLimiterState? Peak = null);
public sealed record OutputDynamicsPlan(Guid Id, OutputDynamicsChange Change, OutputDynamicsState Before, OutputDynamicsState After, long ExpectedRevision, DateTimeOffset ExpiresAt);

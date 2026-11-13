// Core / Output dynamics
// Module: Defines device models, channel state and the contracts shared by all backends.
// Purpose: describe per-output load mode, legacy bridge, per-output noise gate and limiters.
// Units: volts at the output, integer ms, RMS release as an attack multiplier.
// State: maximum voltages are read-only device limits; null gate threshold is unknown.
// Scope: immutable snapshots and revision-bound changes shared by UI and MCP.
namespace CvrTool.Core;

public sealed record RmsLimiterState(bool Enabled, double ThresholdVolts, int AttackMs, int ReleaseMultiplier, bool Automatic, double MaximumVolts);
public sealed record PeakLimiterState(bool Enabled, double ThresholdVolts, int HoldMs, int ReleaseMs, double MaximumVolts);
public sealed record OutputDynamicsState(int Channel, bool Bridged, bool NoiseGateEnabled, int? NoiseGateDb, RmsLimiterState Rms, PeakLimiterState Peak, int? LoadMode = null);
public sealed record OutputDynamicsChange(string DeviceId, int Channel, bool? Bridged = null, bool? NoiseGateEnabled = null, int? NoiseGateDb = null, RmsLimiterState? Rms = null, PeakLimiterState? Peak = null, int? LoadMode = null);
public sealed record OutputDynamicsPlan(Guid Id, OutputDynamicsChange Change, OutputDynamicsState Before, OutputDynamicsState After, long ExpectedRevision, DateTimeOffset ExpiresAt);

// The three editable load modes are separate from legacy bridge configuration.
public static class OutputLoadModes
{
    public static readonly IReadOnlyList<string> Options = Array.AsReadOnly(new[] { "Low-Z", "70V", "100V" });
    public static string Label(int? code) => code switch { null => "Unknown", 0 => "Low-Z", 1 => "70V", 2 => "100V", 3 => "BRDG Low-Z", 4 => "BRDG 70V", 5 => "BRDG 100V", _ => throw new ArgumentOutOfRangeException(nameof(code)) };
}

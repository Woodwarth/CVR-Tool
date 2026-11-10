// Core / Telemetry — nullable readings and status; null always means unknown.
// Purpose: define immutable application data and backend boundaries.
// Input: explicit channel numbers and parameter values.
// State: snapshots describe local or observed device settings.
// Scope: nullable readings remain unknown until measured.
namespace CvrTool.Core;

/// <summary>Per-output binary status. Null means not observed, rather than inactive.</summary>
public sealed record OutputStatus(
    bool? Fault = null,
    bool? Load = null,
    bool? Open = null,
    bool? OverTemperature = null,
    bool? Clip = null,
    bool? Standby = null,
    bool? HighImpedance = null,
    bool? Bridged = null);
/// <summary>Measurements for one channel, with explicit absence of live data.</summary>
public sealed record OutputTelemetry(
    int Channel,
    double? LevelDbFs = null,
    double? Voltage = null,
    double? Current = null,
    double? LoadOhms = null,
    double? TemperatureC = null,
    OutputStatus? Status = null);

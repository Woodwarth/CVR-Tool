// Core / Live device observations
// Module: Defines device models, channel state and the contracts shared by all backends.
// Purpose: expose provenance and known device metadata without invented defaults.
// Units: UTC timestamps; power retains the reported value without assumed units.
// State: DSP-read-only connections retain last successful readings with explicit errors.
// Scope: unsupported meters, identity fields and FIR coefficients stay unknown.
namespace CvrTool.Core;

public sealed record DeviceReadings(
    bool ReadOnly,
    string FirmwareHeader,
    string? MacAddress,
    string? CurrentPreset,
    DateTimeOffset SettingsObservedAt,
    DateTimeOffset? TelemetryObservedAt,
    double? Power,
    IReadOnlyList<int>? StatusCodes,
    string? Error = null,
    bool CanSetDisplayLock = false,
    bool CanSetStandby = false,
    bool CanSetMutes = false,
    uint? OperatingMinutes = null,
    bool CanSetWink = false,
    bool CanSetInputGain = false,
    bool CanSetInputMute = false,
    bool CanRename = false,
    bool CanSetVerifiedDsp = false);

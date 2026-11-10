// Core / Devices — device profiles, snapshots and demo control state.
// Purpose: define immutable application data and backend boundaries.
// Input: explicit channel numbers and parameter values.
// State: snapshots describe local or observed device settings.
// Scope: nullable readings remain unknown until measured.
namespace CvrTool.Core;

// Product variants share all functions; only their configured channel count differs.
public sealed record DeviceCapabilities(
    string Model,
    int OutputCount,
    int? InputCount,
    bool VerifiedOnHardware);
public static class DeviceProfiles
{
    public static DeviceCapabilities Dsp1004 { get; } = new("DSP-1004", 4, 4, false);
    public static DeviceCapabilities Dsp2002 { get; } = new("DSP-2002", 2, 2, false);
    public static DeviceCapabilities Dsp1502 { get; } = new("DSP-1502", 2, 2, false);
}

public sealed record DeviceControlState(bool DisplayLocked = false, bool Standby = false);

public sealed record DeviceSnapshot(
    string Id,
    string Name,
    DeviceCapabilities Capabilities,
    bool IsDemo,
    DateTimeOffset ObservedAt,
    IReadOnlyList<ChannelState> Outputs);

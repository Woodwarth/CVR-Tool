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
    public static IReadOnlyList<DeviceCapabilities> TargetModels { get; } = Array.AsReadOnly(new[]
    {
        "DSP-654", "DSP-802", "DSP-1002", "DSP-1004", "DSP-1502", "DSP-2002",
        "DSP-1504", "DSP-2004", "DSP-3002", "DSP-3004", "DSP-3302", "DSP-4302"
    }.Select(model => new DeviceCapabilities(model, model[^1] - '0', model[^1] - '0', false)).ToArray());
    public static DeviceCapabilities Dsp1004 { get; } = TargetModels.Single(m => m.Model == "DSP-1004");
    public static DeviceCapabilities Dsp2002 { get; } = TargetModels.Single(m => m.Model == "DSP-2002");
    public static DeviceCapabilities Dsp1502 { get; } = TargetModels.Single(m => m.Model == "DSP-1502");

}

public sealed record DeviceControlState(bool DisplayLocked = false, bool Standby = false, bool StandbyKnown = true, bool? Wink = null);

public sealed record DeviceSnapshot(
    string Id,
    string Name,
    DeviceCapabilities Capabilities,
    bool IsDemo,
    DateTimeOffset ObservedAt,
    IReadOnlyList<ChannelState> Outputs,
    DeviceReadings? Readings = null);

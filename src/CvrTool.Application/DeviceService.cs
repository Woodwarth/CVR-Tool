// Application / Devices — validated names and backend-owned operational controls.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class AmplifierService
{
    public const int MaximumPhysicalDeviceNameLength = 31;
    public IReadOnlyList<OutputTelemetry> GetTelemetry(string id) => backend.GetTelemetry(id);
    public DeviceControlState SetWink(string id, bool enabled) => backend.SetWink(id, enabled, backend.GetChannel(id, 1).Revision);
    public DeviceControlState GetControlState(string id) => backend.GetControlState(id);
    public DeviceControlState SetControlState(string id, bool? displayLocked = null, bool? standby = null)
    {
        if (displayLocked is null && standby is null)
            throw new ArgumentException("Supply display lock or standby state.");
        return backend.SetControlState(id, displayLocked, standby, backend.GetChannel(id, 1).Revision);
    }

    public DeviceSnapshot RenameDevice(string id, string name)
    {
        name = backend.GetDevice(id).IsDemo ? NormalizeDeviceName(name) : NormalizePhysicalDeviceName(name);
        var revision = backend.GetChannel(id, 1).Revision;
        return backend.Rename(id, name.Trim(), revision);
    }
    public static string NormalizeDeviceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 32 || name.Any(char.IsControl))
            throw new ArgumentException("Device name must contain 1 to 32 characters without control characters.");
        return name.Trim();
    }
    public static string NormalizePhysicalDeviceName(string name)
    {
        name = NormalizeDeviceName(name);
        if (name.Length > MaximumPhysicalDeviceNameLength || name.Any(c => c > 127))
            throw new ArgumentException("Device-side names require 1 to 31 ASCII characters.");
        return name;
    }
}

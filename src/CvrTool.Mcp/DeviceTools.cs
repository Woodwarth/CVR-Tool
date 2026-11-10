// MCP / Device tools — thin stdio adapters; all behavior lives in Application.
// Purpose: expose the shared application service over stdio MCP.
// Input: typed tool arguments and explicit device identities.
// State: the application session owns devices and revisions.
// Scope: stdout contains protocol data only; logs use stderr.
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CvrTool.Mcp;

public sealed partial class AmplifierTools
{
    [McpServerTool(Name = "list_devices")]
    [Description("List devices in the running application session. Check IsDemo before assuming physical hardware.")]
    public string ListDevices() => Json(service.ListDevices());
    [McpServerTool(Name = "get_device")]
    [Description("Read a device, capabilities and output parameters from the shared session.")]
    public string GetDevice(string deviceId) => Json(service.GetDevice(deviceId));
    [McpServerTool(Name = "rename_device")]
    [Description("Rename a session device in the shared application session. Name: 1–32 characters; physical devices require ASCII and confirmed device-side readback.")]
    public string RenameDevice(string deviceId, string name) => Json(service.RenameDevice(deviceId, name));
    [McpServerTool(Name = "get_telemetry")]
    [Description("Read per-output telemetry. Null fields mean unknown, not measured zero or healthy status.")]
    public string GetTelemetry(string deviceId) => Json(service.GetTelemetry(deviceId));
    [McpServerTool(Name = "get_device_controls")]
    [Description("Read display-lock and standby states through the shared device backend.")]
    public string GetDeviceControls(string deviceId) => Json(service.GetControlState(deviceId));
    [McpServerTool(Name = "set_device_controls")]
    [Description("Set display lock and/or standby through the shared owner. Physical devices require confirmed readback.")]
    public string SetDeviceControls(string deviceId, bool? displayLocked = null, bool? standby = null) => Json(service.SetControlState(deviceId, displayLocked, standby));
}

// MCP / Preset tools — thin stdio adapters; all behavior lives in Application.
// Purpose: expose the shared application service over stdio MCP.
// Input: typed tool arguments and explicit device identities.
// State: the application session owns devices and revisions.
// Scope: stdout contains protocol data only; logs use stderr.
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CvrTool.Mcp;

public sealed partial class AmplifierTools
{
    [McpServerTool(Name = "export_preset")]
    [Description("Return a versioned preset JSON document. Does not write files.")]
    public string ExportPreset(string deviceId) => service.ExportPreset(deviceId);
}

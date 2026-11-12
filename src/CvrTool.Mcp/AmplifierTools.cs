// MCP / Registration — one tool class shared by its focused partial files.
// Purpose: expose the shared application service over stdio MCP.
// Input: typed tool arguments and explicit device identities.
// State: the application session owns devices and revisions.
// Scope: stdout contains protocol data only; logs use stderr.
using System.Text.Json;
using CvrTool.Application;
using ModelContextProtocol.Server;

namespace CvrTool.Mcp;

[McpServerToolType]
public sealed partial class AmplifierTools(AmplifierService service)
{
    private static string Json(object? value) => JsonSerializer.Serialize(value);
}

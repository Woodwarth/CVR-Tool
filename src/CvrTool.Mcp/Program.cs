// MCP / Process entry
// Purpose: expose the same host used by the integrated desktop executable.
// Input: optional session selection or headless-owner command.
// State: all normal tool calls share the running application's backend.
// Scope: stdout contains MCP messages only.
using CvrTool.Mcp;
await McpHost.RunAsync(args);

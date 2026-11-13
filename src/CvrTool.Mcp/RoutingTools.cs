// MCP / Routing tools — thin stdio adapters; all behavior lives in Application.
// Module: Exposes amplifier queries and control operations as MCP tools over stdio.
// Purpose: expose the shared application service over stdio MCP.
// Input: typed tool arguments and explicit device identities.
// State: the application session owns devices and revisions.
// Scope: stdout contains protocol data only; logs use stderr.
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CvrTool.Mcp;

public sealed partial class AmplifierTools
{
    [McpServerTool(Name = "get_routing")]
    [Description("Read the routing matrix. Input topology is not verified on hardware.")]
    public string GetRouting(string deviceId) => Json(service.GetRouting(deviceId));
    [McpServerTool(Name = "plan_routing_change")]
    [Description("Preview a routing cell change. Input/output start at 1; gain -80 to +18 dB. Gain and mute are independent.")]
    public string PlanRoutingChange(string deviceId, int input, int output, double? gainDb = null, bool? muted = null) => Json(service.PlanRouting(new(deviceId, input, output, gainDb, muted)));
    [McpServerTool(Name = "apply_routing_change")]
    [Description("Apply a previewed routing plan UUID. Single use, two-minute expiry and device revision check.")]
    public string ApplyRoutingChange(string planId) => Json(service.ApplyRouting(Guid.Parse(planId)));
}

// MCP / Channel tools — thin stdio adapters; all behavior lives in Application.
// Purpose: expose the shared application service over stdio MCP.
// Input: typed tool arguments and explicit device identities.
// State: the application session owns devices and revisions.
// Scope: stdout contains protocol data only; logs use stderr.
using System.ComponentModel;
using CvrTool.Core;
using ModelContextProtocol.Server;

namespace CvrTool.Mcp;

public sealed partial class AmplifierTools
{
    [McpServerTool(Name = "plan_channel_change")]
    [Description("Validate and preview a channel change. Does not apply it. Gain dB, delay ms. Channel numbers start at 1.")]
    public string PlanChannelChange(string deviceId, int channel, double? gainDb = null, bool? muted = null, double? delayMs = null, bool? inverted = null) => Json(service.Plan(new ChannelChange(deviceId, channel, gainDb, muted, delayMs, inverted)));
    [McpServerTool(Name = "apply_change")]
    [Description("Apply a previously previewed plan by UUID. Single use, expires after two minutes.")]
    public string ApplyChange(string planId) => Json(service.Apply(Guid.Parse(planId)));
}

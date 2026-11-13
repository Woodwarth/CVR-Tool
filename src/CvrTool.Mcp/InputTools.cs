// MCP / Input processing tools
// Module: Exposes amplifier queries and control operations as MCP tools over stdio.
// Purpose: inspect and edit input delay and eight-band EQ.
// Units: milliseconds, hertz, decibels and Q; distance is a UI conversion.
// State: preview/apply shares whole-device revisions with all other edits.
// Scope: identical API on two- and four-channel amplifiers.
using System.ComponentModel;
using CvrTool.Core;
using ModelContextProtocol.Server;

namespace CvrTool.Mcp;

public sealed partial class AmplifierTools
{
    [McpServerTool(Name = "get_input_processing")]
    [Description("Read per-input gain, delay, EQ enable state and eight EQ bands from the shared application session.")]
    public string GetInputProcessing(string deviceId, int channel) => Json(service.GetInputProcessing(deviceId, channel));
    [McpServerTool(Name = "plan_input_processing")]
    [Description("Preview input gain/mute/delay/EQ edits. gainDb is independent of output trim. Delay in ms; EQ band fields: Number, FrequencyHz, GainDb, Q, Enabled. highPass/lowPass: FrequencyHz, FilterType (0–10), Enabled.")]
    public string PlanInputProcessing(string deviceId, int channel, double? delayMs = null, bool? eqEnabled = null, EqualizerBand[]? bands = null, CrossoverFilter? highPass = null, CrossoverFilter? lowPass = null, double? gainDb=null, bool? muted=null) => Json(service.PlanInputProcessing(new(deviceId, channel, delayMs, eqEnabled, bands, highPass, lowPass,gainDb,muted)));
    [McpServerTool(Name = "apply_input_processing")]
    [Description("Apply a previously previewed input processing plan. Single-use, two-minute expiry and revision checks.")]
    public string ApplyInputProcessing(string planId) => Json(service.ApplyInputProcessing(Guid.Parse(planId)));
}

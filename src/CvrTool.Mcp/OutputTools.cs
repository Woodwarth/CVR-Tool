// MCP / Output processing
// Purpose: expose output EQ and FIR through the same session as the desktop.
// Units: ms, Hz, dB, Q and 48 kHz FIR coefficients (maximum 512).
// State: EQ uses revision-bound plans; FIR writes accept an optional revision.
// Scope: tool calls use the selected backend without demo-only restrictions.
using System.ComponentModel;
using CvrTool.Core;
using ModelContextProtocol.Server;

namespace CvrTool.Mcp;

public sealed partial class AmplifierTools
{
    [McpServerTool(Name = "get_output_processing"), Description("Read output delay, eight EQ bands, HP and LP from the shared session.")]
    public string GetOutputProcessing(string deviceId, int channel) => Json(service.GetOutputProcessing(deviceId, channel));
    [McpServerTool(Name = "plan_output_processing"), Description("Preview output EQ/delay. HP/LP FilterType is 0–10, as for input EQ.")]
    public string PlanOutputProcessing(string deviceId, int channel, double? delayMs = null, bool? eqEnabled = null, EqualizerBand[]? bands = null, CrossoverFilter? highPass = null, CrossoverFilter? lowPass = null) => Json(service.PlanOutputProcessing(new(deviceId, channel, delayMs, eqEnabled, bands, highPass, lowPass)));
    [McpServerTool(Name = "apply_output_processing"), Description("Apply a single-use, revision-bound output processing plan within two minutes.")]
    public string ApplyOutputProcessing(string planId) => Json(service.ApplyOutputProcessing(Guid.Parse(planId)));
    [McpServerTool(Name = "get_output_fir"), Description("Read this output's FIR name, enable state and coefficients.")]
    public string GetOutputFir(string deviceId, int channel) => Json(service.GetFir(deviceId, channel));
    [McpServerTool(Name = "set_output_fir"), Description("Set 1–512 finite FIR taps at 48 kHz on one output. Disabled unit impulse removes a filter.")]
    public string SetOutputFir(string deviceId, int channel, string name, double[] coefficients, bool enabled, long? expectedRevision = null) => Json(service.SetFir(deviceId, new(channel, name, coefficients, enabled), expectedRevision));
}

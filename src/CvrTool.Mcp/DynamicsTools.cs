// MCP / Output dynamics
// Purpose: inspect and plan mode, gate and RMS/peak limiter changes.
// Units: output volts, ms, integer dB and RMS release multiplier.
// State: one shared session; plans are revision-bound and single-use.
// Scope: load mode is per output; legacy bridge still applies to A/B or C/D.
using System.ComponentModel;
using CvrTool.Core;
using ModelContextProtocol.Server;
namespace CvrTool.Mcp;

public sealed partial class AmplifierTools
{
    [McpServerTool(Name = "get_output_dynamics"), Description("Read per-output load mode, legacy bridge state, noise gate and RMS/peak limiters with immutable voltage limits.")]
    public string GetOutputDynamics(string deviceId, int channel) => Json(service.GetOutputDynamics(deviceId, channel));
    [McpServerTool(Name = "plan_output_dynamics"), Description("Preview dynamics edits. LoadMode is 0 Low-Z, 1 70V, 2 100V and affects only the selected output. Legacy Bridged affects A/B or C/D; gate -100..0 dB. ThresholdVolts uses actual output volts. MaximumVolts must remain unchanged. RMS ReleaseMultiplier is 2..32 times AttackMs.")]
    public string PlanOutputDynamics(string deviceId, int channel, bool? bridged = null, bool? noiseGateEnabled = null, int? noiseGateDb = null, RmsLimiterState? rms = null, PeakLimiterState? peak = null, int? loadMode = null) => Json(service.PlanOutputDynamics(new(deviceId, channel, bridged, noiseGateEnabled, noiseGateDb, rms, peak, loadMode)));
    [McpServerTool(Name = "apply_output_dynamics"), Description("Apply a revision-bound output dynamics plan within two minutes. Bridge changes both channels in the pair.")]
    public string ApplyOutputDynamics(string planId) => Json(service.ApplyOutputDynamics(Guid.Parse(planId)));
}

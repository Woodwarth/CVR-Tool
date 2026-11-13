// Application / Presets — export observed application state; never import native parameters.
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using System.Text.Json;
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class AmplifierService
{
    public string ExportPreset(string deviceId)
    {
        if(backend is IAdvancedAmplifierBackend advanced)return JsonSerializer.Serialize(advanced.CapturePreset(deviceId),new JsonSerializerOptions{WriteIndented=true});
        var device=backend.GetDevice(deviceId);
        return JsonSerializer.Serialize(new PresetDocument(3,device.Capabilities.Model,device.IsDemo,device.Outputs,device.Name,backend.GetRouting(deviceId).Cells,Enumerable.Range(1,device.Outputs.Count).Select(c=>GetInputProcessing(deviceId,c)).ToArray()),new JsonSerializerOptions{WriteIndented=true});
    }
}

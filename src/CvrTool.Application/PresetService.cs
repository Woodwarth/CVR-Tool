// Application / Presets — export observed application state; never import native parameters.
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
        var device = backend.GetDevice(deviceId);
        IReadOnlyList<RoutingCellState>? routing = null;
        try
        {
            routing = backend.GetRouting(deviceId).Cells;
        }
        catch (NotSupportedException)
        {
        }

        return JsonSerializer.Serialize(new PresetDocument(3, device.Capabilities.Model, device.IsDemo, device.Outputs, device.Name, routing, Enumerable.Range(1, device.Outputs.Count).Select(c => GetInputProcessing(deviceId, c)).ToArray()), new JsonSerializerOptions { WriteIndented = true });
    }
}

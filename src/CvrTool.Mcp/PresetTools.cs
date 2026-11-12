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
    [McpServerTool(Name = "get_native_preset_metadata")]
    [Description("Read preserved native input/speaker names, input-mute flags, source alignment and configured load. Null means no native source; does not write files or devices.")]
    public string GetNativePresetMetadata(string deviceId) => Json(service.GetNativePresetMetadata(deviceId));
    [McpServerTool(Name = "set_output_fir_enabled"), Description("Enable/bypass existing output FIR without changing or inventing coefficients; live writes require independent readback.")]
    public string SetOutputFirEnabled(string deviceId,int channel,bool enabled) => Json(service.SetFirEnabled(deviceId,channel,enabled));
    [McpServerTool(Name = "export_preset")]
    [Description("Return a versioned preset JSON document. Does not write files.")]
    public string ExportPreset(string deviceId) => service.ExportPreset(deviceId);

    [McpServerTool(Name = "export_native_preset"), Description("Return a B-series 118/119 CVR .sd backup as base64; preserves opaque native settings.")]
    public string ExportNativePreset(string deviceId) => Json(new { FileName = "preset.sd", Base64 = Convert.ToBase64String(service.ExportNativePreset(deviceId)) });
    [McpServerTool(Name = "load_preset_file"), Description("Replace processing from a base64 native .sd file. Model/topology must match. Requires confirmed=true.")]
    public string LoadPresetFile(string deviceId, string fileName, string base64, bool confirmed = false)
    {
        RequireConfirmation(confirmed);
        if (base64.Length > 1400000)
            throw new ArgumentException("Preset exceeds size limit.");
        return Json(service.LoadPresetFile(deviceId, fileName, Convert.FromBase64String(base64)));
    }
    [McpServerTool(Name = "get_device_presets"), Description("Read all sixteen on-device preset slots.")]
    public string GetDevicePresets(string deviceId) => Json(service.GetDevicePresets(deviceId));
    [McpServerTool(Name = "store_device_preset"), Description("Store current processing in slot 1–16, replacing that slot. Requires confirmed=true.")]
    public string StoreDevicePreset(string deviceId, int slot, string name, bool confirmed = false)
    {
        RequireConfirmation(confirmed);
        return Json(service.StoreDevicePreset(deviceId, slot, name));
    }
    [McpServerTool(Name = "recall_device_preset"), Description("Replace current processing with device slot 1–16. Requires confirmed=true.")]
    public string RecallDevicePreset(string deviceId, int slot, bool confirmed = false)
    {
        RequireConfirmation(confirmed);
        return Json(service.RecallDevicePreset(deviceId, slot));
    }
    [McpServerTool(Name = "clear_device_presets"), Description("Permanently clear all sixteen device presets. Requires confirmed=true; current processing stays active.")]
    public string ClearDevicePresets(string deviceId, bool confirmed = false)
    {
        RequireConfirmation(confirmed);
        return Json(service.ClearDevicePresets(deviceId));
    }
    [McpServerTool(Name = "factory_reset"), Description("Restore factory processing; presets may be cleared. Requires confirmed=true.")]
    public string FactoryReset(string deviceId, bool confirmed = false)
    {
        RequireConfirmation(confirmed);
        return Json(service.FactoryReset(deviceId));
    }
    private static void RequireConfirmation(bool confirmed)
    {
        if (!confirmed)
            throw new ArgumentException("Explicit confirmation is required for this bulk/destructive operation.");
    }
}

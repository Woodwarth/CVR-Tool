// Checks / Per-output load modes
// Purpose: distinguish Low-Z/70V/100V from legacy paired bridge settings.
// Units: native mode codes; no real amplifier is contacted.
// State: plans, schema migration and file/slot restoration preserve channel isolation.
// Scope: bridge-coded native values are retained but not offered as new mode edits.
using CvrTool.Application;
using CvrTool.Core;
using System.Text.Json;
static class LoadModeChecks
{
    public static void Run(Action<bool,string> check)
    {
        var service=new AmplifierService(new DemoBackend());const string id="demo-1004";
        void Reject(Action action,string message){try{action();}catch(ArgumentException){check(true,message);return;}throw new Exception(message);}
        foreach(var code in new[]{0,1,2})
        {
            var before=service.GetOutputDynamics(id,1);var plan=service.PlanOutputDynamics(new(id,1,LoadMode:code));
            check(service.GetOutputDynamics(id,1)==before,"Load-mode preview is read-only");service.ApplyOutputDynamics(plan.Id);
            check(service.GetOutputDynamics(id,1).LoadMode==code && service.GetOutputDynamics(id,2).LoadMode==0 && !service.GetOutputDynamics(id,1).Bridged,"Load mode is per output and does not switch legacy bridge");
        }
        check(OutputLoadModes.Options.SequenceEqual(new[]{"Low-Z","70V","100V"}),"Exactly three editable output mode labels");
        foreach(var code in new[]{-1,3,4,5,6})Reject(()=>service.PlanOutputDynamics(new(id,1,LoadMode:code)),"Unlisted modes cannot be newly requested");
        var preset=JsonSerializer.Deserialize<PresetDocument>(service.ExportPreset(id))!;var native=service.ExportNativePreset(id);
        check(native[36+367]==2 && native[36+515+367]==0 && native[34]==1,"Native mode is at channel offset 367, not the bridge-pair field");
        var restored=new AmplifierService(new DemoBackend());restored.LoadPresetFile(id,"mode.sd",native);check(restored.GetOutputDynamics(id,1).LoadMode==2 && restored.ExportNativePreset(id).SequenceEqual(native),"Native mode round-trips exactly");
        service.StoreDevicePreset(id,1,"100V");service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,LoadMode:0)).Id);service.RecallDevicePreset(id,1);check(service.GetOutputDynamics(id,1).LoadMode==2,"Device preset restores load mode");
        var legacy=preset with {SchemaVersion=5,Dynamics=preset.Dynamics!.Select(d=>d with {LoadMode=null}).ToArray()};service.LoadPreset(id,legacy);check(service.GetOutputDynamics(id,1).LoadMode==2,"Schema 5 without native bytes retains current load mode");
        restored.LoadPreset(id,legacy with {NativeData=native});check(restored.GetOutputDynamics(id,1).LoadMode==2,"Schema 5 with native bytes recovers original mode");
        foreach(var raw in new byte[]{3,4,5})
        {
            var file=native.ToArray();file[36+367]=raw;restored.LoadPresetFile(id,"legacy.sd",file);
            check(restored.GetOutputDynamics(id,1).LoadMode==raw && restored.ExportNativePreset(id).SequenceEqual(file),"Existing bridge-coded mode is preserved without reinterpretation");
        }
    }
}

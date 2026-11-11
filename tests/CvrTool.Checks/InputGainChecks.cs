// Checks / Independent input gain
// Purpose: keep captured input gain separate from output trim and preserve presets.
// Units: dB, native float32 at channel offset 369; no physical device is contacted.
// State: plans remain revision-bound and nullable legacy gains retain current state.
// Scope: local models, native codec and application validation only.
using CvrTool.Application;
using CvrTool.Core;
using System.Text.Json;
static class InputGainChecks
{
    public static void Run(Action<bool,string> check)
    {
        var service=new AmplifierService(new DemoBackend());const string id="demo-1004";
        void Reject(Action action,string label){try{action();}catch(ArgumentException){check(true,label);return;}catch(NotSupportedException){check(true,label);return;}catch(InvalidOperationException){check(true,label);return;}throw new Exception(label);}
        var before=service.GetInputProcessing(id,1);var plan=service.PlanInputProcessing(new(id,1,GainDb:4));
        check(before.GainDb==0 && service.GetInputProcessing(id,1).GainDb==0 && plan.After.GainDb==4,"Input gain preview performs no writes");
        service.ApplyInputProcessing(plan.Id);
        check(service.GetInputProcessing(id,1).GainDb==4 && service.GetInputProcessing(id,2).GainDb==0 && service.GetDevice(id).Outputs[0].GainDb==0,"Input gain is independent of output trim and other inputs");
        check(service.GetInputProcessing(id,1).Bands.SequenceEqual(before.Bands) && service.GetInputProcessing(id,1).DelayMs==before.DelayMs,"Gain-only edit preserves input delay and EQ");
        Reject(()=>service.ApplyInputProcessing(plan.Id),"Input gain plans remain single use");
        foreach(var value in new[]{double.NaN,double.PositiveInfinity,-80.1,18.1})Reject(()=>service.PlanInputProcessing(new(id,1,GainDb:value)),"Invalid input gain rejected");
        Reject(()=>AmplifierService.MergeInput(before with {GainDb=null},new(id,1,GainDb:4)),"Unobserved input gain is not invented");
        Reject(()=>service.PlanOutputProcessing(new(id,1,GainDb:4)),"Output processing cannot alias the input gain field");
        var stale=service.PlanInputProcessing(new(id,1,GainDb:4.5));service.Apply(service.Plan(new(id,1,GainDb:-10)).Id);Reject(()=>service.ApplyInputProcessing(stale.Id),"Output edit invalidates an earlier input plan");
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,GainDb:4.5)).Id);
        var json=service.ExportPreset(id);var preset=JsonSerializer.Deserialize<PresetDocument>(json)!;check(preset.SchemaVersion==7 && preset.Inputs![0].GainDb==4.5 && preset.Outputs[0].GainDb==-10,"Schema 7 exports separate input and output gains");
        var native=service.ExportNativePreset(id);check(BitConverter.ToSingle(native,36+369)==4.5f && BitConverter.ToSingle(native,36+44)==-10f,"Native input and output gains occupy independent known offsets");
        var syncOnly = native[..NativePresetCodec.Inspect(native).SyncLength];
        check(NativePresetCodec.Encode(NativePresetCodec.Decode(syncOnly)).SequenceEqual(syncOnly), "Sync-only native preset does not invent FIR blocks or a footer");
        var restored=new AmplifierService(new DemoBackend());restored.LoadPresetFile(id,"capture.sd",native);check(restored.GetInputProcessing(id,1).GainDb==4.5 && restored.GetDevice(id).Outputs[0].GainDb==-10 && restored.ExportNativePreset(id).SequenceEqual(native),"Native gain round-trip preserves all bytes");
        service.StoreDevicePreset(id,1,"Gain");service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,GainDb:0)).Id);service.RecallDevicePreset(id,1);check(service.GetInputProcessing(id,1).GainDb==4.5,"Device preset restores input gain");
        var legacy=preset with {SchemaVersion=6,Inputs=preset.Inputs!.Select(s=>s with {GainDb=null}).ToArray()};service.LoadPreset(id,legacy);check(service.GetInputProcessing(id,1).GainDb==4.5,"Legacy JSON without gain retains active input gain");
        restored.LoadPreset(id,legacy with {NativeData=native});check(restored.GetInputProcessing(id,1).GainDb==4.5,"Legacy JSON with native bytes recovers original input gain");
    }
}

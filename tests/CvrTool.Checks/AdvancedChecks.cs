// Checks / Full processing and presets
// Purpose: exercise independent EQ/FIR, bulk validation and file/slot restoration.
// Units: normal application units; synthetic native bytes never leave the test.
// State: checks include stale revisions, aliasing and failures before mutation.
// Scope: local models and file codecs; no physical device claims.
using System.Text.Json;
using CvrTool.Application;
using CvrTool.Core;

static class AdvancedChecks
{
    public static void Run(Action<bool,string> check)
    {
        var backend=new DemoBackend();var service=new AmplifierService(backend);const string id="demo-1004";
        void Reject(Action action,string label) {try{action();}catch(ArgumentException){check(true,label);return;}catch(InvalidOperationException){check(true,label);return;}catch(NotSupportedException){check(true,label);return;}throw new Exception(label);}
        var input=service.GetInputProcessing(id,1);
        var bands=service.GetOutputProcessing(id,1).Bands.Select(b=>b with {GainDb=b.Number==1 ? 6 : 0}).ToArray();
        var plan=service.PlanOutputProcessing(new(id,1,DelayMs:3,EqEnabled:false,Bands:bands,HighPass:new(80,6,true)));
        bands[0]=bands[0] with {GainDb=18};
        check(service.GetOutputProcessing(id,1).DelayMs==0 && plan.After.Bands[0].GainDb==6,"Output EQ preview freezes values and performs no writes");
        service.ApplyOutputProcessing(plan.Id);
        check(service.GetOutputProcessing(id,1) is {DelayMs:3,EqEnabled:false} && service.GetDevice(id).Outputs[0].DelayMs==3 && service.GetInputProcessing(id,1).Bands.SequenceEqual(input.Bands) && service.GetOutputProcessing(id,2).Bands[0].GainDb==0,"Output EQ is independent of inputs and other outputs; delay has one authority");
        Reject(()=>service.ApplyOutputProcessing(plan.Id),"Output plan is single use");
        var stale=service.PlanOutputProcessing(new(id,1,DelayMs:4));service.SetFir(id,new(1,"Delay",new double[]{0,1},true));
        Reject(()=>service.ApplyOutputProcessing(stale.Id),"FIR writes invalidate output EQ plans");
        var fir=service.GetFir(id,1);((double[])fir.Coefficients)[0]=100;
        check(service.GetFir(id,1).Coefficients[0]==0 && service.GetFir(id,2).Coefficients[0]==1,"FIR snapshots are defensive and isolated");
        foreach(var taps in new[]{Array.Empty<double>(),new double[513],new[]{double.NaN},new[]{double.PositiveInfinity}})
            Reject(()=>service.SetFir(id,new(1,"invalid",taps)),"Invalid FIR rejected before writing");
        Reject(()=>service.SetFir(id,new(1,"rate",new double[]{1},true,44100)),"FIR rejects incompatible sample rate");
        check(service.GetFir(id,1).Name=="Delay","Rejected FIR leaves device unchanged");
        var json=service.ExportPreset(id);var captured=JsonSerializer.Deserialize<PresetDocument>(json)!;
        check(captured is {SchemaVersion:7,OutputProcessing.Count:4,Firs.Count:4,Inputs.Count:4,Routing.Count:16},"JSON captures full supported processing");
        service.Apply(service.Plan(new(id,1,GainDb:-20)).Id);service.LoadPresetFile(id,"preset.json",System.Text.Encoding.UTF8.GetBytes(json));
        check(service.GetDevice(id).Outputs[0].GainDb==captured.Outputs[0].GainDb && service.GetFir(id,1).Name=="Delay","JSON restores full session");
        foreach(var slot in Enumerable.Range(1,16))service.StoreDevicePreset(id,slot,"Slot "+slot);
        check(service.GetDevicePresets(id).Slots.All(s=>s.Occupied) && service.GetDevicePresets("demo-1502").Slots.All(s=>!s.Occupied),"All sixteen slots store independently per device");
        service.SetFir(id,new(1,"Changed",new double[]{1},false));service.RecallDevicePreset(id,16);
        check(service.GetFir(id,1).Name=="Delay" && service.GetOutputProcessing(id,1).Bands[0].GainDb==6,"Device recall restores FIR and output EQ");
        foreach(var slot in new[]{0,17})Reject(()=>service.StoreDevicePreset(id,slot,"Bad"),"Invalid preset slot rejected");
        var native=service.ExportNativePreset(id);var info=NativePresetCodec.Inspect(native);
        check(info is {Count:4,SyncLength:2687,FirLength:2080,Model:"DSP-1004"},"Native export has bounded B119 layout");
        native[36+59]=0x7A;native[36+410]=0x4D; // Opaque limiter and dynamic-EQ bytes.
        service.LoadPresetFile(id,"native.sd",native);
        check(service.ExportNativePreset(id).SequenceEqual(native),"Native load/export preserves opaque bytes and exact processing");
        service.Apply(service.Plan(new(id,1,GainDb:-13)).Id);var changed=service.ExportNativePreset(id);
        check(changed[36+59]==0x7A && changed[36+410]==0x4D && NativePresetCodec.Decode(changed).Outputs[0].GainDb==-13,"Native edits preserve unexposed parameters");
        var b118=native[..2232].Concat(native[2687..]).ToArray();System.Text.Encoding.ASCII.GetBytes("106118").CopyTo(b118,9);
        service.LoadPresetFile(id,"118.sd",b118);check(service.ExportNativePreset(id).SequenceEqual(b118),"B118 layout round-trips independently");
        var noNames=native[..2687].Concat(Enumerable.Range(0,4).SelectMany(n=>native[(2687+n*2080+32)..(2687+(n+1)*2080)])).Concat(new byte[16]).ToArray();
        System.Text.Encoding.ASCII.GetBytes("FIR==2048").CopyTo(noNames,noNames.Length-16);
        service.LoadPresetFile(id,"legacy.sd",noNames);check(service.ExportNativePreset(id).SequenceEqual(noNames),"Legacy unnamed FIR layout round-trips");
        Reject(()=>service.LoadPresetFile("demo-1502","wrong.sd",native),"Native model mismatch rejected");
        var unknown=native.ToArray();unknown[9]=(byte)'9';Reject(()=>service.LoadPresetFile(id,"unknown.sd",unknown),"Unknown native firmware rejected");
        Reject(()=>service.LoadPreset(id,captured with {Firs=[new(1,"Invalid",new double[]{double.NaN})]}),"Invalid bulk data rejected before mutation");
        check(service.GetDevice(id).Outputs[0].GainDb==0,"Failed bulk operation leaves existing output untouched");
        service.ClearDevicePresets(id);check(service.GetDevicePresets(id).Slots.All(s=>!s.Occupied) && service.GetOutputProcessing(id,1).Bands[0].GainDb==6,"Clear removes slots and retains active processing");
        Reject(()=>service.RecallDevicePreset(id,1),"Empty slot cannot be recalled");
        service.FactoryReset(id);check(service.GetFir(id,1) is {Enabled:false,Name:""} && service.GetOutputProcessing(id,1).Bands[0].GainDb==0 && service.GetDevicePresets(id).Slots.Count==16,"Factory reset restores processing and clears slots");
    }
}

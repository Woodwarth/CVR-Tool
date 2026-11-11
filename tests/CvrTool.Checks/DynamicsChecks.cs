// Checks / Output dynamics
// Purpose: verify paired mode, independent gates/limiters and preset restoration.
// Units: output volts, ms and integer dB; tests never connect to real devices.
// State: invalid and stale plans leave state unchanged; limits remain immutable.
// Scope: local model, native layouts and legacy JSON compatibility.
using CvrTool.Application;
using CvrTool.Core;
using System.Text.Json;
static class DynamicsChecks
{
    public static void Run(Action<bool,string> check)
    {
        var backend=new DemoBackend();var service=new AmplifierService(backend);const string id="demo-1004";
        void Reject(Action action,string message){try{action();}catch(ArgumentException){check(true,message);return;}catch(InvalidOperationException){check(true,message);return;}throw new Exception(message);}
        var first=service.GetOutputDynamics(id,1);var plan=service.PlanOutputDynamics(new(id,2,Bridged:true,NoiseGateEnabled:true,NoiseGateDb:-70));
        check(!service.GetOutputDynamics(id,2).Bridged,"Dynamics preview performs no writes");service.ApplyOutputDynamics(plan.Id);
        check(service.GetOutputDynamics(id,1).Bridged && service.GetOutputDynamics(id,2).Bridged && !service.GetOutputDynamics(id,3).Bridged,"Bridge applies to A/B without changing C/D");
        check(!service.GetOutputDynamics(id,1).NoiseGateEnabled && service.GetOutputDynamics(id,2) is {NoiseGateEnabled:true,NoiseGateDb:-70},"Noise gate remains per output");
        Reject(()=>service.ApplyOutputDynamics(plan.Id),"Dynamics plans are single use");
        var stale=service.PlanOutputDynamics(new(id,1,Bridged:false));service.SetFir(id,new(1,"Edit",new double[]{1}));Reject(()=>service.ApplyOutputDynamics(stale.Id),"Other writes invalidate dynamics plans");
        foreach(var db in new[]{-101,1})Reject(()=>service.PlanOutputDynamics(new(id,1,NoiseGateDb:db)),"Gate range is -100 through 0 dB");
        foreach(var rms in new[]{first.Rms with {ThresholdVolts=double.NaN},first.Rms with {ThresholdVolts=first.Rms.MaximumVolts+1},first.Rms with {MaximumVolts=1000},first.Rms with {AttackMs=0},first.Rms with {AttackMs=2001},first.Rms with {ReleaseMultiplier=1},first.Rms with {ReleaseMultiplier=33}})
            Reject(()=>service.PlanOutputDynamics(new(id,1,Rms:rms)),"Invalid RMS settings rejected before mutation");
        foreach(var peak in new[]{first.Peak with {ThresholdVolts=double.PositiveInfinity},first.Peak with {HoldMs=-1},first.Peak with {HoldMs=2001},first.Peak with {ReleaseMs=19},first.Peak with {ReleaseMs=1001}})
            Reject(()=>service.PlanOutputDynamics(new(id,1,Peak:peak)),"Invalid peak settings rejected before mutation");
        var rmsState=first.Rms with {ThresholdVolts=60,AttackMs=40,ReleaseMultiplier=8,Automatic=true,Enabled=false};var peakState=first.Peak with {ThresholdVolts=100,HoldMs=100,ReleaseMs=400,Enabled=true};
        service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,Rms:rmsState,Peak:peakState)).Id);
        check(service.GetOutputDynamics(id,1).Rms==rmsState && service.GetOutputDynamics(id,1).Peak==peakState && service.GetOutputDynamics(id,2).Rms==first.Rms,"Peak and RMS settings apply independently per output");
        var json=service.ExportPreset(id);var preset=JsonSerializer.Deserialize<PresetDocument>(json)!;check(preset is {SchemaVersion:7,Dynamics.Count:4},"Schema 7 exports complete dynamics");
        service.StoreDevicePreset(id,1,"Dynamics");service.FactoryReset(id);service.LoadPreset(id,preset);check(service.GetOutputDynamics(id,1).Rms==rmsState && service.GetOutputDynamics(id,2).NoiseGateDb==-70,"JSON restores mode, gate and both limiters");
        service.StoreDevicePreset(id,1,"Dynamics");service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,Bridged:false)).Id);service.RecallDevicePreset(id,1);check(service.GetOutputDynamics(id,1).Bridged,"Device preset restores paired mode");
        var clock=new ManualClock();var timed=new AmplifierService(new DemoBackend(),clock);var expiring=timed.PlanOutputDynamics(new(id,1,NoiseGateDb:-80));clock.Now=clock.Now.AddMinutes(2);Reject(()=>timed.ApplyOutputDynamics(expiring.Id),"Dynamics plans expire after two minutes");
        var native=service.ExportNativePreset(id);var decoded=NativePresetCodec.Decode(native);check(decoded.Dynamics![0].Rms==rmsState && decoded.Dynamics[1].NoiseGateDb==-70,"Native codec encodes limiter and gate values");
        check(native[34]==0 && native[36+373]==1 && unchecked((sbyte)native[2233])==-70 && BitConverter.ToInt16(native,36+59)==40,"Native byte offsets and active-low flags match independent expectations");
        service.LoadPresetFile(id,"native.sd",native);check(service.ExportNativePreset(id).SequenceEqual(native),"Dynamics native round-trip preserves exact bytes");
        var legacy=preset with {SchemaVersion=4,Dynamics=null};service.LoadPreset(id,legacy);check(service.GetOutputDynamics(id,1).Rms==rmsState,"Legacy JSON retains current dynamics");
        Reject(()=>service.LoadPreset(id,preset with {Dynamics=preset.Dynamics!.Select(d=>d.Channel==2 ? d with {Bridged=false} : d).ToArray()}),"Inconsistent bridge pairs rejected before bulk writes");
        service.FactoryReset(id);check(!service.GetOutputDynamics(id,1).Bridged && service.GetOutputDynamics(id,1).NoiseGateDb==-100,"Factory reset restores dynamics defaults");
    }
}

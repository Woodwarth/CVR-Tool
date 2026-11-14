// Checks / Live view fixture
// Purpose: exercise automatic device presentation, read-only controls and offline state.
// Units: synthetic native DSP values; timestamps are generated locally.
// State: live fixture coexists with all three unchanged demo devices.
// Scope: no network sockets or real amplifier addresses are used by UI checks.
using CvrTool.Application;
using CvrTool.Core;

sealed class LiveDesktopFixture : IAmplifierBackend, IAdvancedAmplifierBackend
{
    public const string LiveId = "cvr:127.0.0.2:45455";
    private readonly DemoBackend demo = new();
    public LiveDesktopFixture() { demo.AddDemo("DSP-2002"); }
    public double TestVoltage { get; set; } = 40;
    public string? Error { get; set; }
    public string LiveName { get; set; } = "Live fixture";
    private static string Map(string id) => id == LiveId ? "demo-1004" : id;
    private static NotSupportedException NoWrite() => new("Read-only fixture");
    public DeviceSnapshot GetDevice(string id) => id != LiveId ? demo.GetDevice(id) : demo.GetDevice("demo-1004") with
    {
        Id = LiveId, Name = LiveName, IsDemo = false,
        Readings = new(true, "Fixture-106119-DSP-1004", "02:00:00:00:00:01", "Fixture preset", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 25, [8,8,8,8], Error, CanSetDisplayLock:true, CanSetStandby:true, CanSetMutes:true, OperatingMinutes:26259, CanSetWink:true, CanSetInputGain:true, CanSetInputMute:true, CanRename:true, CanSetVerifiedDsp:true)
    };
    public IReadOnlyList<DeviceSnapshot> ListDevices() => demo.ListDevices().Append(GetDevice(LiveId)).ToArray();
    public (ChannelState State,long Revision) GetChannel(string id,int channel) => demo.GetChannel(Map(id),channel);
    public IReadOnlyList<OutputTelemetry> GetTelemetry(string id) => demo.GetTelemetry(Map(id)).Select(t => t with { InputMeterDb = null, InputClip = null }).Select(t => t.Channel == 1 ? t with { Voltage=TestVoltage,Current=5,TemperatureC=42,Status=new(Clip:true) } : t).ToArray();
    public RoutingSnapshot GetRouting(string id) => demo.GetRouting(Map(id)) with {DeviceId=id};
    public (InputProcessingState State,long Revision) GetInputProcessing(string id,int channel) => demo.GetInputProcessing(Map(id),channel);
    public DeviceControlState GetControlState(string id) => demo.GetControlState(Map(id));
    public (InputProcessingState State,long Revision) GetOutputProcessing(string id,int channel) => demo.GetOutputProcessing(Map(id),channel);
    public (OutputDynamicsState State,long Revision) GetOutputDynamics(string id,int channel) => demo.GetOutputDynamics(Map(id),channel);
    public (FirState State,long Revision) GetFir(string id,int channel) => (new(channel,"Unknown",[],CoefficientsKnown:false),0);
    public DevicePresetState GetDevicePresets(string id) => id == LiveId ? new([],"Fixture preset") : demo.GetDevicePresets(id);
    public PresetDocument CapturePreset(string id) => demo.CapturePreset(Map(id));
    public ChannelState Apply(ChannelChange change,long revision) => demo.Apply(change with {DeviceId=Map(change.DeviceId)},revision);
    public RoutingCellState ApplyRouting(RoutingChange change,long revision) => demo.ApplyRouting(change with {DeviceId=Map(change.DeviceId)},revision);
    public DeviceSnapshot Rename(string id,string name,long revision) { demo.Rename(Map(id),name,revision); if (id==LiveId) LiveName=name; return GetDevice(id); }
    public InputProcessingState ApplyInputProcessing(InputProcessingChange change,long revision) => demo.ApplyInputProcessing(change with { DeviceId=Map(change.DeviceId) },revision);
    public InputProcessingState ApplyOutputProcessing(InputProcessingChange change,long revision) => throw NoWrite();
    public FirState ApplyFirEnabled(string id,int channel,bool enabled,long revision) => demo.ApplyFirEnabled(Map(id),channel,enabled,revision);
    public FirState ApplyFir(string id,FirState state,long revision) => throw NoWrite();
    public OutputDynamicsState ApplyOutputDynamics(OutputDynamicsChange change,long revision) => demo.ApplyOutputDynamics(change with {DeviceId=Map(change.DeviceId)},revision);
    public DeviceControlState SetControlState(string id,bool? locked,bool? standby,long revision) => throw NoWrite();
    public DeviceSnapshot ApplyPreset(string id,PresetDocument preset,long revision) => throw NoWrite();
    public DeviceOperationResult StoreDevicePreset(string id,int slot,string name,long revision) => throw NoWrite();
    public DeviceOperationResult RecallDevicePreset(string id,int slot,long revision) => throw NoWrite();
    public DeviceOperationResult FactoryReset(string id,long revision) => throw NoWrite();
    public DeviceOperationResult ClearDevicePresets(string id,long revision) => throw NoWrite();
}

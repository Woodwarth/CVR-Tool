// Application / Session client
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: implement the backend contract against the running application session.
// Input: strongly typed operations; replies preserve validation failures.
// State: no private device copy and no independent revision counters.
// Scope: current-user-only IPC, bounded messages and bounded request lifetime.
using System.IO.Pipes;
using System.Text.Json;
using CvrTool.Core;

namespace CvrTool.Application;

public sealed class SessionBackend(string? pipeName = null) : IDemoManagement, IAmplifierBackend, IConnectionBackend, IAdvancedAmplifierBackend, INativePresetMetadataSource
{
    private T Call<T>(string method, object arguments) where T : class => CallAsync<T>(method, arguments).GetAwaiter().GetResult();
    private async Task<T> CallAsync<T>(string method, object arguments) where T : class
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var pipe = new NamedPipeClientStream(".", SessionDefaults.Resolve(pipeName), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await SessionWire.WriteAsync(pipe, new SessionRequest(method, JsonSerializer.SerializeToElement(arguments)), timeout.Token).ConfigureAwait(false);
            var reply = await SessionWire.ReadAsync<SessionReply>(pipe, timeout.Token).ConfigureAwait(false);
            if (reply.Error is { } error)
                throw reply.ErrorType switch
                {
                    nameof(ArgumentOutOfRangeException) => new ArgumentOutOfRangeException(null, error),
                    nameof(ArgumentException) => new ArgumentException(error),
                    nameof(NotSupportedException) => new NotSupportedException(error),
                    _ => new InvalidOperationException(error)
                };
            return reply.Result?.Deserialize<T>() ?? throw new InvalidDataException("Session result missing.");
        }
        catch (OperationCanceledException) { throw new InvalidOperationException("CVR Tool session is unavailable. Start the desktop app or a session host first."); }
    }
    public DeviceSnapshot AddDemo(string model) => Call<DeviceSnapshot>("add-demo",new { Model = model });
    public void RemoveDemo(string id) => Call<string>("remove-demo",new { Id = id });
    public IReadOnlyList<DeviceSnapshot> ListDevices() => Call<DeviceSnapshot[]>("list", new { });
    public DeviceSnapshot Connect(CvrTool.Protocol.NetworkTarget target) => Call<DeviceSnapshot>("connect", new { Target = target });
    public DisconnectionResult Disconnect(string deviceId) => Call<DisconnectionResult>("disconnect", new { Id = deviceId });
    public DeviceSnapshot GetDevice(string deviceId) => Call<DeviceSnapshot>("device", new { Id = deviceId });
    public (ChannelState State, long Revision) GetChannel(string deviceId, int channel)
    {
        var read = Call<Revisioned<ChannelState>>("channel", new
        {
            Id = deviceId,
            Channel = channel
        });
        return (read.State, read.Revision);
    }
    public ChannelState Apply(ChannelChange change, long expectedRevision) => Call<ChannelState>("apply-channel", new { Change = change, Revision = expectedRevision });
    public RoutingSnapshot GetRouting(string deviceId) => Call<RoutingSnapshot>("routing", new { Id = deviceId });
    public RoutingCellState ApplyRouting(RoutingChange change, long expectedRevision) => Call<RoutingCellState>("apply-routing", new { Change = change, Revision = expectedRevision });
    public DeviceSnapshot Rename(string deviceId, string name, long expectedRevision) => Call<DeviceSnapshot>("rename", new { Id = deviceId, Name = name, Revision = expectedRevision });
    public IReadOnlyList<OutputTelemetry> GetTelemetry(string deviceId) => Call<OutputTelemetry[]>("telemetry", new { Id = deviceId });
    public DeviceControlState SetWink(string id, bool enabled, long revision) => Call<DeviceControlState>("wink", new { Id = id, Enabled = enabled, Revision = revision });
    public DeviceControlState GetControlState(string deviceId) => Call<DeviceControlState>("controls", new { Id = deviceId });
    public DeviceControlState SetControlState(string deviceId, bool? displayLocked, bool? standby, long expectedRevision) => Call<DeviceControlState>("set-controls", new { Id = deviceId, Locked = displayLocked, Standby = standby, Revision = expectedRevision });
    public (InputProcessingState State, long Revision) GetInputProcessing(string deviceId, int channel)
    {
        var read = Call<Revisioned<InputProcessingState>>("input", new
        {
            Id = deviceId,
            Channel = channel
        });
        return (read.State, read.Revision);
    }
    public InputProcessingState ApplyInputProcessing(InputProcessingChange change, long expectedRevision) => Call<InputProcessingState>("apply-input", new { Change = change, Revision = expectedRevision });
    public (InputProcessingState State,long Revision) GetOutputProcessing(string id,int channel) {var result=Call<Revisioned<InputProcessingState>>("output-processing",new {Id=id,Channel=channel});return(result.State,result.Revision);}
    public InputProcessingState ApplyOutputProcessing(InputProcessingChange change,long revision)=>Call<InputProcessingState>("apply-output-processing",new{Change=change,Revision=revision});
    public (FirState State,long Revision) GetFir(string id,int channel) {var result=Call<Revisioned<FirState>>("fir",new {Id=id,Channel=channel});return(result.State,result.Revision);}
    public FirState ApplyFirEnabled(string id,int channel,bool enabled,long revision) => Call<FirState>("apply-fir-enabled",new {Id=id,Channel=channel,Enabled=enabled,Revision=revision});
    public FirState ApplyFir(string id,FirState state,long revision)=>Call<FirState>("apply-fir",new{Id=id,State=state,Revision=revision});
    public (OutputDynamicsState State,long Revision) GetOutputDynamics(string id,int channel) {var read=Call<Revisioned<OutputDynamicsState>>("output-dynamics",new{Id=id,Channel=channel});return(read.State,read.Revision);}
    public OutputDynamicsState ApplyOutputDynamics(OutputDynamicsChange change,long revision)=>Call<OutputDynamicsState>("apply-output-dynamics",new{Change=change,Revision=revision});
    public string InspectProtocol(string id, int function, int channel, int segment)
        => Call<string>("inspect-protocol", new { Id = id, Function = function, Channel = channel, Segment = segment });
    public NativePresetMetadata? GetNativePresetMetadata(string id) => Call<NativeMetadataReply>("native-metadata", new { Id = id }).Metadata;
    public PresetDocument CapturePreset(string id)=>Call<PresetDocument>("capture-preset",new{Id=id});
    public DeviceSnapshot ApplyPreset(string id,PresetDocument preset,long revision)=>Call<DeviceSnapshot>("apply-preset",new{Id=id,Preset=preset,Revision=revision});
    public DevicePresetState GetDevicePresets(string id)=>Call<DevicePresetState>("preset-slots",new{Id=id});
    public DeviceOperationResult StoreDevicePreset(string id,int slot,string name,long revision)=>Call<DeviceOperationResult>("store-preset",new{Id=id,Slot=slot,Name=name,Revision=revision});
    public DeviceOperationResult RecallDevicePreset(string id,int slot,long revision)=>Call<DeviceOperationResult>("recall-preset",new{Id=id,Slot=slot,Revision=revision});
    public DeviceOperationResult FactoryReset(string id,long revision)=>Call<DeviceOperationResult>("factory-reset",new{Id=id,Revision=revision});
    public DeviceOperationResult ClearDevicePresets(string id,long revision)=>Call<DeviceOperationResult>("clear-presets",new{Id=id,Revision=revision});
}


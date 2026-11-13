// Application / Session owner
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: expose one backend instance to all local clients.
// Input: framed backend operations over a current-user-only pipe.
// State: backend writes own revision checks; notifications follow successful mutations.
// Scope: no duplicate demo instance is created for a connecting MCP client.
using System.IO.Pipes;
using System.Text.Json;
using CvrTool.Core;

namespace CvrTool.Application;

public sealed class SessionHost : IDisposable
{
    private readonly IAmplifierBackend backend;
    private readonly string pipeName;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task listener;
    private readonly McpClientPresenceHost mcpPresence;
    public event Action? Changed;
    public event Action? McpClientsChanged;
    public int McpClientCount => mcpPresence.Count;
    public SessionHost(IAmplifierBackend backend, string? pipeName = null)
    {
        this.backend = backend;
        this.pipeName = SessionDefaults.Resolve(pipeName);
        // Reserve ownership synchronously so a second owner fails before accepting UI writes.
        var first = CreatePipe();
        try { mcpPresence = new McpClientPresenceHost(pipeName); }
        catch { first.Dispose(); shutdown.Dispose(); throw; }
        mcpPresence.Changed += () => McpClientsChanged?.Invoke();
        listener = Task.Run(() => ListenAsync(first));
    }
    private NamedPipeServerStream CreatePipe() => new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    private async Task ListenAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                while (!shutdown.IsCancellationRequested)
                {
                    var accepted = false;
                    try
                    {
                        await pipe.WaitForConnectionAsync(shutdown.Token);
                        accepted = true;
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(120));
                        SessionReply reply;
                        try
                        {
                            var request = await SessionWire.ReadAsync<SessionRequest>(pipe, timeout.Token);
                            reply = new(JsonSerializer.SerializeToElement(Dispatch(request)));
                        }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or JsonException or IOException or TimeoutException or System.Net.Sockets.SocketException or KeyNotFoundException)
                        {
                            reply = new(null, ex.Message, ex is KeyNotFoundException ? nameof(ArgumentException) : ex.GetType().Name);
                        }
                        await SessionWire.WriteAsync(pipe, reply, timeout.Token);
                    }
                    catch (OperationCanceledException) when (!shutdown.IsCancellationRequested) { }
                    catch (IOException) when (!shutdown.IsCancellationRequested) { }
                    finally
                    {
                        // Keep the listening endpoint alive across calls. Recreating a Unix socket
                        // can discard clients queued just after the previous reply was sent.
                        if (accepted) pipe.Disconnect();
                    }
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            catch (IOException) when (shutdown.IsCancellationRequested) { }
        }
    }
    private object Dispatch(SessionRequest request)
    {
        var args = request.Arguments;
        string Id() => args.GetProperty("Id").GetString() ?? throw new ArgumentException("Device ID missing.");
        string RemoveDemo(string id) { ((IDemoManagement)backend).RemoveDemo(id); return id; }
        int Channel() => args.GetProperty("Channel").GetInt32();
        long Revision() => args.GetProperty("Revision").GetInt64();
        T Change<T>() => args.GetProperty("Change").Deserialize<T>() ?? throw new ArgumentException("Change missing.");
        var advanced=backend as IAdvancedAmplifierBackend;
        object result = request.Method switch
        {
            "add-demo" => ((IDemoManagement)backend).AddDemo(args.GetProperty("Model").GetString()!),
            "remove-demo" => RemoveDemo(Id()),
            "list" => backend.ListDevices(),
            "output-dynamics" => DynamicsSnapshot(Id(),Channel()),
            "apply-output-dynamics" => advanced!.ApplyOutputDynamics(Change<OutputDynamicsChange>(),Revision()),
            "output-processing" => OutputSnapshot(Id(),Channel()),
            "apply-output-processing" => advanced!.ApplyOutputProcessing(Change<InputProcessingChange>(),Revision()),
            "fir" => FirSnapshot(Id(),Channel()),
            "apply-fir-enabled" => advanced!.ApplyFirEnabled(Id(),Channel(),args.GetProperty("Enabled").GetBoolean(),Revision()),
            "apply-fir" => advanced!.ApplyFir(Id(),args.GetProperty("State").Deserialize<FirState>()!,Revision()),
            "inspect-protocol" => ((INativePresetMetadataSource)backend).InspectProtocol(Id(), args.GetProperty("Function").GetInt32(), Channel(), args.GetProperty("Segment").GetInt32()),
            "native-metadata" => new NativeMetadataReply((backend as INativePresetMetadataSource)?.GetNativePresetMetadata(Id())),
            "capture-preset" => advanced!.CapturePreset(Id()),
            "apply-preset" => advanced!.ApplyPreset(Id(),args.GetProperty("Preset").Deserialize<PresetDocument>()!,Revision()),
            "preset-slots" => advanced!.GetDevicePresets(Id()),
            "store-preset" => advanced!.StoreDevicePreset(Id(),args.GetProperty("Slot").GetInt32(),args.GetProperty("Name").GetString()!,Revision()),
            "recall-preset" => advanced!.RecallDevicePreset(Id(),args.GetProperty("Slot").GetInt32(),Revision()),
            "factory-reset" => advanced!.FactoryReset(Id(),Revision()),
            "clear-presets" => advanced!.ClearDevicePresets(Id(),Revision()),

            "connect" => (backend as IConnectionBackend ?? throw new NotSupportedException("Connections unavailable.")).Connect(args.GetProperty("Target").Deserialize<CvrTool.Protocol.NetworkTarget>() ?? throw new ArgumentException("Target missing.")),
            "disconnect" => (backend as IConnectionBackend ?? throw new NotSupportedException("Connections unavailable.")).Disconnect(Id()),
            "device" => backend.GetDevice(Id()),
            "channel" => ChannelSnapshot(Id(), Channel()),
            "routing" => backend.GetRouting(Id()),
            "input" => InputSnapshot(Id(), Channel()),
            "telemetry" => backend.GetTelemetry(Id()),
            "wink" => backend.SetWink(Id(), args.GetProperty("Enabled").GetBoolean(), Revision()),
            "controls" => backend.GetControlState(Id()),
            "apply-channel" => backend.Apply(Change<ChannelChange>(), Revision()),
            "apply-routing" => backend.ApplyRouting(Change<RoutingChange>(), Revision()),
            "apply-input" => backend.ApplyInputProcessing(Change<InputProcessingChange>(), Revision()),
            "rename" => backend.Rename(Id(), args.GetProperty("Name").GetString() ?? "", Revision()),
            "set-controls" => backend.SetControlState(Id(), args.GetProperty("Locked").Deserialize<bool?>(), args.GetProperty("Standby").Deserialize<bool?>(), Revision()),
            _ => throw new ArgumentException("Unknown session operation.")
        };
        if (request.Method.StartsWith("apply-", StringComparison.Ordinal) || request.Method is "add-demo" or "remove-demo" or "wink" or "rename" or "set-controls" or "connect" or "disconnect" or "store-preset" or "recall-preset" or "factory-reset" or "clear-presets")
            Changed?.Invoke();
        return result;
    }
    private Revisioned<ChannelState> ChannelSnapshot(string id, int channel)
    {
        var state = backend.GetChannel(id, channel);
        return new(state.State, state.Revision);
    }
    private Revisioned<InputProcessingState> InputSnapshot(string id, int channel)
    {
        var state = backend.GetInputProcessing(id, channel);
        return new(state.State, state.Revision);
    }
    private Revisioned<InputProcessingState> OutputSnapshot(string id,int channel) {var read=((IAdvancedAmplifierBackend)backend).GetOutputProcessing(id,channel);return new(read.State,read.Revision);}
    private Revisioned<FirState> FirSnapshot(string id,int channel) {var read=((IAdvancedAmplifierBackend)backend).GetFir(id,channel);return new(read.State,read.Revision);}
    private Revisioned<OutputDynamicsState> DynamicsSnapshot(string id,int channel) {var read=((IAdvancedAmplifierBackend)backend).GetOutputDynamics(id,channel);return new(read.State,read.Revision);}
    public void Dispose()
    {
        mcpPresence.Dispose();
        shutdown.Cancel();
        try
        {
            listener.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        shutdown.Dispose();
    }
}

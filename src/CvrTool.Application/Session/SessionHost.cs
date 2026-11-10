// Application / Session owner
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
    public event Action? Changed;
    public SessionHost(IAmplifierBackend backend, string? pipeName = null)
    {
        this.backend = backend;
        this.pipeName = SessionDefaults.Resolve(pipeName);
        // Reserve ownership synchronously so a second owner fails before accepting UI writes.
        var first = CreatePipe();
        listener = Task.Run(() => ListenAsync(first));
    }
    private NamedPipeServerStream CreatePipe() => new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    private async Task ListenAsync(NamedPipeServerStream first)
    {
        var pipe = first;
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                using (pipe)
                {
                    try
                    {
                        await pipe.WaitForConnectionAsync(shutdown.Token);
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
                }
                if (shutdown.IsCancellationRequested)
                    break;
                pipe = CreatePipe();
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (IOException) when (shutdown.IsCancellationRequested) { }
        finally { pipe.Dispose(); }
    }
    private object Dispatch(SessionRequest request)
    {
        var args = request.Arguments;
        string Id() => args.GetProperty("Id").GetString() ?? throw new ArgumentException("Device ID missing.");
        int Channel() => args.GetProperty("Channel").GetInt32();
        long Revision() => args.GetProperty("Revision").GetInt64();
        T Change<T>() => args.GetProperty("Change").Deserialize<T>() ?? throw new ArgumentException("Change missing.");
        object result = request.Method switch
        {
            "list" => backend.ListDevices(),
            "connect" => (backend as IConnectionBackend ?? throw new NotSupportedException("Connections unavailable.")).Connect(args.GetProperty("Target").Deserialize<CvrTool.Protocol.NetworkTarget>() ?? throw new ArgumentException("Target missing.")),
            "disconnect" => (backend as IConnectionBackend ?? throw new NotSupportedException("Connections unavailable.")).Disconnect(Id()),
            "device" => backend.GetDevice(Id()),
            "channel" => ChannelSnapshot(Id(), Channel()),
            "routing" => backend.GetRouting(Id()),
            "input" => InputSnapshot(Id(), Channel()),
            "telemetry" => backend.GetTelemetry(Id()),
            "controls" => backend.GetControlState(Id()),
            "apply-channel" => backend.Apply(Change<ChannelChange>(), Revision()),
            "apply-routing" => backend.ApplyRouting(Change<RoutingChange>(), Revision()),
            "apply-input" => backend.ApplyInputProcessing(Change<InputProcessingChange>(), Revision()),
            "rename" => backend.Rename(Id(), args.GetProperty("Name").GetString() ?? "", Revision()),
            "set-controls" => backend.SetControlState(Id(), args.GetProperty("Locked").Deserialize<bool?>(), args.GetProperty("Standby").Deserialize<bool?>(), Revision()),
            _ => throw new ArgumentException("Unknown session operation.")
        };
        if (request.Method.StartsWith("apply-", StringComparison.Ordinal) || request.Method is "rename" or "set-controls" or "connect" or "disconnect")
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
    public void Dispose()
    {
        shutdown.Cancel();
        try
        {
            listener.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        shutdown.Dispose();
    }
}

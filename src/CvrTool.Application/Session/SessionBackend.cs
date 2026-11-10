// Application / Session client
// Purpose: implement the backend contract against the running application session.
// Input: strongly typed operations; replies preserve validation failures.
// State: no private device copy and no independent revision counters.
// Scope: current-user-only IPC, bounded messages and bounded request lifetime.
using System.IO.Pipes;
using System.Text.Json;
using CvrTool.Core;

namespace CvrTool.Application;

public sealed class SessionBackend(string? pipeName = null) : IAmplifierBackend, IConnectionBackend
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
}


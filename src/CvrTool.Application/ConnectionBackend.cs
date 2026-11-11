// Application / Connection ownership
// Purpose: keep local models and explicit CVR peers in one shared session.
// Input: manual IPv4 connection settings, never a discovery range.
// State: one transport owner per target; disconnect disposes its socket.
// Scope: the application session is the only writer used by desktop and MCP.
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed record DisconnectionResult(bool Disconnected);
public interface IConnectionBackend
{
    DeviceSnapshot Connect(NetworkTarget target);
    DisconnectionResult Disconnect(string deviceId);
}

public sealed class ConnectionBackend : IAmplifierBackend, IConnectionBackend, IAdvancedAmplifierBackend, IDisposable
{
    private readonly DemoBackend local = new();
    private readonly Dictionary<string, CvrLanBackend> peers = new();
    private readonly object gate = new();
    private IAmplifierBackend Backend(string id) => peers.TryGetValue(id, out var peer) ? peer : local;
    public DeviceSnapshot Connect(NetworkTarget target)
    {
        lock (gate)
        {
            target = NetworkPreparation.Validate(target).Target;
            var id = $"cvr:{target.RemoteAddress}:{target.RemotePort}";
            if (peers.ContainsKey(id))
                return peers[id].GetDevice(id);
            var backend = new CvrLanBackend(target);
            try
            {
                var device = backend.GetDevice(id);
                peers.Add(id, backend);
                return device;
            }
            catch { backend.Dispose(); throw; }
        }
    }
    public DisconnectionResult Disconnect(string deviceId)
    {
        lock (gate)
        {
            if (!peers.Remove(deviceId, out var peer))
                return new(false);
            peer.Dispose();
            return new(true);
        }
    }
    public IReadOnlyList<DeviceSnapshot> ListDevices()
    {
        lock (gate)
            return local.ListDevices().Concat(peers.Values.SelectMany(p => p.ListDevices())).ToArray();
    }
    public DeviceSnapshot GetDevice(string id)
    {
        lock (gate)
            return Backend(id).GetDevice(id);
    }
    public (ChannelState State, long Revision) GetChannel(string id, int channel)
    {
        lock (gate)
            return Backend(id).GetChannel(id, channel);
    }
    public ChannelState Apply(ChannelChange change, long revision)
    {
        lock (gate)
            return Backend(change.DeviceId).Apply(change, revision);
    }
    public RoutingSnapshot GetRouting(string id)
    {
        lock (gate)
            return Backend(id).GetRouting(id);
    }
    public RoutingCellState ApplyRouting(RoutingChange change, long revision)
    {
        lock (gate)
            return Backend(change.DeviceId).ApplyRouting(change, revision);
    }
    public DeviceSnapshot Rename(string id, string name, long revision)
    {
        lock (gate)
            return Backend(id).Rename(id, name, revision);
    }
    public IReadOnlyList<OutputTelemetry> GetTelemetry(string id)
    {
        lock (gate)
            return Backend(id).GetTelemetry(id);
    }
    public (InputProcessingState State, long Revision) GetInputProcessing(string id, int channel)
    {
        lock (gate)
            return Backend(id).GetInputProcessing(id, channel);
    }
    public InputProcessingState ApplyInputProcessing(InputProcessingChange change, long revision)
    {
        lock (gate)
            return Backend(change.DeviceId).ApplyInputProcessing(change, revision);
    }
    public DeviceControlState GetControlState(string id)
    {
        lock (gate)
            return Backend(id).GetControlState(id);
    }
    public DeviceControlState SetControlState(string id, bool? locked, bool? standby, long revision)
    {
        lock (gate)
            return Backend(id).SetControlState(id, locked, standby, revision);
    }
    public (InputProcessingState State,long Revision) GetOutputProcessing(string id,int channel) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).GetOutputProcessing(id,channel); }
    public InputProcessingState ApplyOutputProcessing(InputProcessingChange change,long revision) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(change.DeviceId)).ApplyOutputProcessing(change,revision); }
    public (FirState State,long Revision) GetFir(string id,int channel) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).GetFir(id,channel); }
    public FirState ApplyFir(string id,FirState state,long revision) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).ApplyFir(id,state,revision); }
    public (OutputDynamicsState State,long Revision) GetOutputDynamics(string id,int channel) {lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).GetOutputDynamics(id,channel);}
    public OutputDynamicsState ApplyOutputDynamics(OutputDynamicsChange change,long revision) {lock(gate)return ((IAdvancedAmplifierBackend)Backend(change.DeviceId)).ApplyOutputDynamics(change,revision);}
    public PresetDocument CapturePreset(string id) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).CapturePreset(id); }
    public DeviceSnapshot ApplyPreset(string id,PresetDocument preset,long revision) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).ApplyPreset(id,preset,revision); }
    public DevicePresetState GetDevicePresets(string id) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).GetDevicePresets(id); }
    public DeviceOperationResult StoreDevicePreset(string id,int slot,string name,long revision) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).StoreDevicePreset(id,slot,name,revision); }
    public DeviceOperationResult RecallDevicePreset(string id,int slot,long revision) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).RecallDevicePreset(id,slot,revision); }
    public DeviceOperationResult FactoryReset(string id,long revision) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).FactoryReset(id,revision); }
    public DeviceOperationResult ClearDevicePresets(string id,long revision) { lock(gate)return ((IAdvancedAmplifierBackend)Backend(id)).ClearDevicePresets(id,revision); }
    public void Dispose()
    {
        lock (gate)
        {
            foreach (var peer in peers.Values)
                peer.Dispose();
            peers.Clear();
        }
    }
}

public sealed partial class AmplifierService
{
    public DeviceSnapshot Connect(NetworkTarget target) => (backend as IConnectionBackend ?? throw new NotSupportedException("This backend does not own connections.")).Connect(target);
    public DisconnectionResult Disconnect(string id) => (backend as IConnectionBackend ?? throw new NotSupportedException("This backend does not own connections.")).Disconnect(id);
}

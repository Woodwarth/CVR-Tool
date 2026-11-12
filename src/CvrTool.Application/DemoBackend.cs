// Application / Demo session — shared state, snapshots and channel writes; no hardware.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using CvrTool.Core;

namespace CvrTool.Application;
/// <summary>In-memory simulation. No sockets, discovery or real devices.</summary>
public sealed partial class DemoBackend : IAmplifierBackend, INativePresetMetadataSource
{
    private readonly object gate = new();
    private readonly Dictionary<string, RoutingCellState[]> routing = new()
    {
        ["demo-1004"] = CreateRouting(4),
        ["demo-1502"] = CreateRouting(2),
        ["demo-2002"] = CreateRouting(2)
    };
    private static RoutingCellState[] CreateRouting(int count) => Enumerable.Range(1, count).SelectMany(output => Enumerable.Range(1, count).Select(input => new RoutingCellState(input, output, 0, input != output))).ToArray();
    private readonly Dictionary<string, (DeviceSnapshot Device, long Revision)> devices = new()
    {
        ["demo-1004"] = (Create("demo-1004", DeviceProfiles.Dsp1004), 0),
        ["demo-2002"] = (Create("demo-2002", DeviceProfiles.Dsp2002), 0),
        ["demo-1502"] = (Create("demo-1502", DeviceProfiles.Dsp1502), 0)
    };
    private static DeviceSnapshot Create(string id, DeviceCapabilities profile) => new(id, profile.Model + " · Demo", profile, true, DateTimeOffset.UtcNow, Enumerable.Range(1, profile.OutputCount).Select(n => new ChannelState(n, ((char)('A' + n - 1)).ToString(), 0, true, 0, false)).ToArray());
    private static DeviceSnapshot Copy(DeviceSnapshot device) => device with
    {
        Outputs = device.Outputs.ToArray()
    };
    private (DeviceSnapshot Device, long Revision) Find(string id) => devices.TryGetValue(id, out var entry) ? entry : throw new ArgumentException("Unknown device ID.", nameof(id));
    private static ChannelState FindChannel(DeviceSnapshot device, int channel) => device.Outputs.SingleOrDefault(c => c.Number == channel) ?? throw new ArgumentOutOfRangeException(nameof(channel), "Channel does not exist on this model.");
    public IReadOnlyList<DeviceSnapshot> ListDevices()
    {
        lock (gate)
            return devices.Values.Select(e => Copy(e.Device)).ToArray();
    }

    public DeviceSnapshot GetDevice(string deviceId)
    {
        lock (gate)
            return Copy(Find(deviceId).Device);
    }

    public (ChannelState State, long Revision) GetChannel(string deviceId, int channel)
    {
        lock (gate)
        {
            var entry = Find(deviceId);
            return (FindChannel(entry.Device, channel), entry.Revision);
        }
    }

    public ChannelState Apply(ChannelChange change, long expectedRevision)
    {
        lock (gate)
        {
            var entry = Find(change.DeviceId);
            if (entry.Revision != expectedRevision)
                throw new InvalidOperationException("Device changed. Create a new plan.");
            var state = AmplifierService.Merge(FindChannel(entry.Device, change.Channel), change);
            devices[change.DeviceId] = (entry.Device with
            {
                Outputs = entry.Device.Outputs.Select(c => c.Number == change.Channel ? state : c).ToArray(),
                ObservedAt = DateTimeOffset.UtcNow
            }, entry.Revision + 1);
            return state;
        }
    }
}

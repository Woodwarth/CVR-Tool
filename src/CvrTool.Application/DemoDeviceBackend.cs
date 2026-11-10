// Application / Demo devices — names, lock/standby and unknown telemetry; no sockets.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class DemoBackend
{
    public DeviceSnapshot Rename(string deviceId, string name, long expectedRevision)
    {
        lock (gate)
        {
            var entry = Find(deviceId);
            if (entry.Revision != expectedRevision)
                throw new InvalidOperationException("Device changed. Try renaming again.");
            var result = entry.Device with
            {
                Name = AmplifierService.NormalizeDeviceName(name),
                ObservedAt = DateTimeOffset.UtcNow
            };
            devices[deviceId] = (result, entry.Revision + 1);
            return Copy(result);
        }
    }

    private readonly Dictionary<string, DeviceControlState> controls = new();
    public DeviceControlState GetControlState(string deviceId)
    {
        lock (gate)
        {
            Find(deviceId);
            return controls.GetValueOrDefault(deviceId) ?? new();
        }
    }

    public DeviceControlState SetControlState(string deviceId, bool? displayLocked, bool? standby, long expectedRevision)
    {
        lock (gate)
        {
            if (displayLocked is null && standby is null)
                throw new ArgumentException("Supply a control state.");
            var entry = Find(deviceId);
            if (entry.Revision != expectedRevision)
                throw new InvalidOperationException("Device changed. Try the control again.");
            var before = GetControlState(deviceId);
            var after = before with
            {
                DisplayLocked = displayLocked ?? before.DisplayLocked,
                Standby = standby ?? before.Standby
            };
            controls[deviceId] = after;
            devices[deviceId] = (entry.Device with
            {
                ObservedAt = DateTimeOffset.UtcNow
            }, entry.Revision + 1);
            return after;
        }
    }

    public IReadOnlyList<OutputTelemetry> GetTelemetry(string deviceId)
    {
        lock (gate)
            return Find(deviceId).Device.Outputs.Select(c => new OutputTelemetry(c.Number, Status: new OutputStatus())).ToArray();
    }
}

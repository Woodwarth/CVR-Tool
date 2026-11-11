// Core / Backend — storage and transport boundary used by the application service.
// Purpose: define immutable application data and backend boundaries.
// Input: explicit channel numbers and parameter values.
// State: snapshots describe local or observed device settings.
// Scope: nullable readings remain unknown until measured.
namespace CvrTool.Core;

/// <summary>Backend implementations return snapshots and enforce expected revisions on writes.</summary>
public interface IAmplifierBackend
{
    IReadOnlyList<DeviceSnapshot> ListDevices();
    DeviceSnapshot GetDevice(string deviceId);
    (ChannelState State, long Revision) GetChannel(string deviceId, int channel);
    ChannelState Apply(ChannelChange change, long expectedRevision);
    RoutingSnapshot GetRouting(string deviceId);
    RoutingCellState ApplyRouting(RoutingChange change, long expectedRevision);
    DeviceSnapshot Rename(string deviceId, string name, long expectedRevision);
    IReadOnlyList<OutputTelemetry> GetTelemetry(string deviceId);
    (InputProcessingState State, long Revision) GetInputProcessing(string deviceId, int channel);
    InputProcessingState ApplyInputProcessing(InputProcessingChange change, long expectedRevision);
    DeviceControlState SetWink(string deviceId, bool enabled, long expectedRevision) => throw new NotSupportedException("Wink is unavailable on this backend.");
    DeviceControlState GetControlState(string deviceId);
    DeviceControlState SetControlState(string deviceId, bool? displayLocked, bool? standby, long expectedRevision);
}

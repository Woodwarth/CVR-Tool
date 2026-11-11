// Core / Output processing and preset storage
// Purpose: share output EQ, FIR, native files and sixteen preset slots across clients.
// Units: milliseconds, Hz, dB, Q and 48 kHz FIR coefficients.
// State: backend writes enforce device revisions; snapshots are defensive copies.
// Scope: unsupported native layouts fail explicitly rather than dropping unknown data.
namespace CvrTool.Core;

public sealed record FirState(int Channel, string Name, IReadOnlyList<double> Coefficients, bool Enabled = false, int SampleRate = 48000);
public sealed record DevicePresetSlot(int Number, string Name, bool Occupied);
public sealed record DevicePresetState(IReadOnlyList<DevicePresetSlot> Slots, string? CurrentName = null);
public sealed record DeviceOperationResult(string Operation, bool Confirmed, string Message);
public interface IAdvancedAmplifierBackend
{
    (InputProcessingState State, long Revision) GetOutputProcessing(string id, int channel);
    InputProcessingState ApplyOutputProcessing(InputProcessingChange change, long revision);
    (FirState State, long Revision) GetFir(string id, int channel);
    FirState ApplyFir(string id, FirState state, long revision);
    PresetDocument CapturePreset(string id);
    DeviceSnapshot ApplyPreset(string id, PresetDocument preset, long revision);
    DevicePresetState GetDevicePresets(string id);
    DeviceOperationResult StoreDevicePreset(string id, int slot, string name, long revision);
    DeviceOperationResult RecallDevicePreset(string id, int slot, long revision);
    DeviceOperationResult FactoryReset(string id, long revision);
    DeviceOperationResult ClearDevicePresets(string id, long revision);
}

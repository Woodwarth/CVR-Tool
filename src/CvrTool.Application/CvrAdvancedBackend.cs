// Application / Physical output processing and preset operations
// Purpose: implement observed EQ/FIR, native backup and sixteen-slot commands.
// Units: packed wire blocks; one-based application slots become zero-based wire slots.
// State: serialize operations, invalidate revisions and require device readback.
// Scope: no physical commands run during automated checks; firmware compatibility remains pending.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class CvrLanBackend : IAdvancedAmplifierBackend
{
    public (InputProcessingState State, long Revision) GetOutputProcessing(string deviceId, int channel)
    {
        lock (gate)
        {
            CheckId(deviceId);
            var ch = Channel(channel);
            var delay = Float(Read(14, ch, side: 1));
            var state = ProcessingWire.DecodeEq(channel, delay, Read(52, ch, side: 1));
            return (state, revision);
        }
    }
    public InputProcessingState ApplyOutputProcessing(InputProcessingChange change, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(change.DeviceId);
            var before = GetOutputProcessing(change.DeviceId, change.Channel).State;
            Expected(expectedRevision);
            var after = AmplifierService.MergeInput(before, change);
            var ch = Channel(change.Channel);
            if (change.DelayMs is not null)
                Write(14, FloatBytes(after.DelayMs), ch, side: 1);
            if (change.Bands is not null || change.EqEnabled is not null || change.HighPass is not null || change.LowPass is not null)
                Write(52, ProcessingWire.EncodeEq(after, Read(52, ch, side: 1)), ch, side: 1);
            return GetOutputProcessing(change.DeviceId, change.Channel).State;
        }
    }
    public (FirState State, long Revision) GetFir(string deviceId, int channel)
    {
        lock (gate)
        {
            CheckId(deviceId);
            var ch = Channel(channel);
            var enabled = !Flag(Read(44, ch, side: 1));
            return (ProcessingWire.DecodeFir(channel, Read(43, ch, side: 1), enabled), revision);
        }
    }
    public FirState ApplyFir(string deviceId, FirState state, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(deviceId);
            state = AmplifierService.ValidateFir(state);
            var current = GetFir(deviceId, state.Channel);
            Expected(expectedRevision);
            var ch = Channel(state.Channel);
            if (state.Name.Any(c => c > 127))
                throw new ArgumentException("Device FIR names currently require ASCII.");
            var existing = Read(43, ch, side: 1);
            Write(43, ProcessingWire.EncodeFir(state, existing.Length == 2080), ch, side: 1);
            Write(44, [(byte)(state.Enabled ? 0 : 1)], ch, side: 1);
            return GetFir(deviceId, state.Channel).State;
        }
    }
    private byte[] CaptureNative(string deviceId)
    {
        CheckId(deviceId);
        var sync = Read(27);
        if (sync.Length is not (2232 or 2687))
            throw new NotSupportedException("Native backup supports the evidenced B-series 118/119 sync layouts only.");
        using var data = new MemoryStream();
        data.Write(sync);
        var length = 0;
        for (var channel = 1; channel <= capabilities.OutputCount; channel++)
        {
            var fir = Read(43, Channel(channel), side: 1);
            if (fir.Length is not (2048 or 2080) || (length != 0 && length != fir.Length))
                throw new NotSupportedException("Inconsistent FIR layout.");
            length = fir.Length;
            data.Write(fir);
        }
        var footer = new byte[16];
        System.Text.Encoding.ASCII.GetBytes("FIR==" + length).CopyTo(footer, 0);
        data.Write(footer);
        var file = data.ToArray();
        NativePresetCodec.Inspect(file);
        return file;
    }
    public PresetDocument CapturePreset(string deviceId)
    {
        lock (gate)
            return PresetOperations.Capture(this, this, deviceId, CaptureNative(deviceId));
    }
    public DeviceSnapshot ApplyPreset(string deviceId, PresetDocument preset, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(deviceId);
            preset = PresetOperations.Validate(preset, GetDevice(deviceId));
            var current = CaptureNative(deviceId);
            Expected(expectedRevision);
            foreach (var state in preset.Dynamics ?? [])
                MergeLimiterLimits(state);
            Expected(expectedRevision);
            var file = NativePresetCodec.Encode(preset with
            {
                NativeData = preset.NativeData ?? current
            });
            var info = NativePresetCodec.Inspect(file);
            if (!file.AsSpan(0, 16).SequenceEqual(current.AsSpan(0, 16)))
                throw new NotSupportedException("Native preset firmware/power/topology header differs from this device.");
            revision++;
            client.Write(27, file[..info.SyncLength]);
            var observed = client.Read(27);
            if (!observed.SequenceEqual(file[..info.SyncLength]))
                throw new InvalidOperationException("Preset sync readback differs; changes may be partial. Refresh the device.");
            for (var channel = 1; channel <= info.Count; channel++)
                Write(43, file[(info.SyncLength + (channel - 1) * info.FirLength)..(info.SyncLength + channel * info.FirLength)], Channel(channel), side: 1);
            if(info.SyncLength==2232)
                foreach(var state in preset.Dynamics ?? [])
                    if(state.NoiseGateDb is { } db)Write(87,[unchecked((byte)(sbyte)db)],Channel(state.Channel),side:1);
            return GetDevice(deviceId);
        }
    }
    private void MergeLimiterLimits(OutputDynamicsState state)
    {
        var actual = GetOutputDynamics(id, state.Channel).State;
        AmplifierService.MergeDynamics(actual, new(id, state.Channel, Rms: state.Rms, Peak: state.Peak));
    }
    public DevicePresetState GetDevicePresets(string deviceId)
    {
        lock (gate)
        {
            CheckId(deviceId);
            var body = client.Read(59, requestBody: new byte[34]);
            if (body.Length != 512)
                throw new NotSupportedException("Expected sixteen 32-byte preset names.");
            return new(Enumerable.Range(0, 16).Select(n => { var name = Text(body[(n * 32)..((n + 1) * 32)]); return new DevicePresetSlot(n + 1, string.IsNullOrWhiteSpace(name) ? "Empty" : name, !string.IsNullOrWhiteSpace(name)); }).ToArray());
        }
    }
    public DeviceOperationResult StoreDevicePreset(string deviceId, int slot, string name, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(deviceId);
            PresetOperations.CheckSlot(slot);
            Expected(expectedRevision);
            name = AmplifierService.NormalizeDeviceName(name);
            if (name.Any(c => c > 127))
                throw new ArgumentException("Device preset names require ASCII.");
            var body = new byte[34];
            body[0] = 1;
            body[1] = (byte)(slot - 1);
            ProcessingWire.PutText(body, 2, name, 32);
            revision++;
            client.Command(59, body);
            if (GetDevicePresets(deviceId).Slots[slot - 1].Name != name)
                throw new InvalidOperationException("Stored preset name was not confirmed.");
            return new("Store", true, $"Device acknowledged preset {slot}; name readback confirmed.");
        }
    }
    public DeviceOperationResult RecallDevicePreset(string deviceId, int slot, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(deviceId);
            PresetOperations.CheckSlot(slot);
            Expected(expectedRevision);
            if (!GetDevicePresets(deviceId).Slots[slot - 1].Occupied)
                throw new InvalidOperationException("Preset slot is empty.");
            var body = new byte[34];
            body[0] = 2;
            body[1] = (byte)(slot - 1);
            revision++;
            client.Command(59, body);
            GetDevice(deviceId);
            return new("Recall", true, $"Device acknowledged recall {slot}; output settings reloaded.");
        }
    }
    public DeviceOperationResult ClearDevicePresets(string deviceId, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(deviceId);
            Expected(expectedRevision);
            var body = new byte[34];
            body[0] = 3;
            revision++;
            client.Command(59, body);
            if (GetDevicePresets(deviceId).Slots.Any(s => s.Occupied))
                throw new InvalidOperationException("Device preset clear was not confirmed.");
            return new("Clear", true, "All sixteen device slots read back empty.");
        }
    }
    public DeviceOperationResult FactoryReset(string deviceId, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(deviceId);
            Expected(expectedRevision);
            revision++;
            client.Command(16, [1]);
            observations.Clear();
            GetDevice(deviceId);
            GetDevicePresets(deviceId);
            return new("Factory reset", true, "Device acknowledged reset; current settings and preset names reloaded.");
        }
    }
}

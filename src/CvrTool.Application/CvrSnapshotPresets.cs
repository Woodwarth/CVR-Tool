// Application / Physical presets
// Loads compatible native settings and operates the reported device slots.
// Uses the existing UDP owner and confirms changes with fresh device reads.
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend
{
    public DeviceSnapshot ApplyPreset(string deviceId, PresetDocument preset, long expectedRevision)
    {
        MutateSettings(deviceId, expectedRevision, client =>
        {
            preset = PresetOperations.Validate(preset, GetDevice(deviceId));
            var file = NativePresetCodec.Encode(preset);
            var layout = NativePresetCodec.Inspect(file);
            if (layout.SyncLength != native.Length || !file.AsSpan(0, 32).SequenceEqual(native.AsSpan(0, 32)))
                throw new NotSupportedException("Preset firmware, power profile or model differs from this amplifier.");
            foreach (var dynamics in preset.Dynamics ?? [])
                AmplifierService.MergeDynamics(settings.Dynamics![dynamics.Channel - 1], new(deviceId, dynamics.Channel, Rms: dynamics.Rms, Peak: dynamics.Peak));
            var expected = file[..layout.SyncLength];
            native.AsSpan(32, 2).CopyTo(expected.AsSpan(32)); // Preserve standby and panel lock.
            if (!expected.SequenceEqual(native))
            {
                client.Write(27, expected);
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    ReadSettings(client);
                    // A PC recall sets the current scenario label on the observed DSP-1004.
                    bool sameLabel = native.AsSpan(2160,32).SequenceEqual(expected.AsSpan(2160,32));
                    bool pcLabel = Text(native[2160..2192]) is "PC Recall" or "(M)PC Recall";
                    if ((sameLabel || pcLabel) && native.AsSpan(0,2160).SequenceEqual(expected.AsSpan(0,2160)) &&
                        native.AsSpan(2192).SequenceEqual(expected.AsSpan(2192))) break;
                    if (wait.Elapsed.TotalSeconds >= 3) throw new InvalidOperationException("Preset readback differs. Device settings may be partially applied.");
                    Thread.Sleep(100);
                }
            }
            for (int index = 0; layout.FirLength > 0 && index < layout.Count; index++)
            {
                var raw = file.AsSpan(layout.SyncLength + index * layout.FirLength, layout.FirLength).ToArray();
                if (raw.Length == 2048) raw = new byte[32].Concat(raw).ToArray();
                var old = client.ReadInspection(43, (byte)index, 0, 1);
                if (old.SequenceEqual(raw)) continue;
                client.Write(43, raw, (byte)index, side: 1);
                var readback = client.ReadInspection(43, (byte)index, 0, 1);
                if (!readback.SequenceEqual(raw)) throw new InvalidOperationException($"FIR readback differs on output {index+1}. Preset may be partially applied.");
                lock (gate) firByChannel[index+1] = ProcessingWire.DecodeFir(index+1, raw, false);
            }
        });
        return GetDevice(deviceId);
    }

    public DeviceOperationResult StoreDevicePreset(string deviceId,int slot,string name,long expectedRevision) =>
        ChangePreset(deviceId,1,slot,name,expectedRevision);
    public DeviceOperationResult RecallDevicePreset(string deviceId,int slot,long expectedRevision) =>
        ChangePreset(deviceId,2,slot,"",expectedRevision);
    public DeviceOperationResult ClearDevicePresets(string deviceId,long expectedRevision) =>
        ChangePreset(deviceId,3,0,"",expectedRevision);

    private DeviceOperationResult ChangePreset(string id, byte mode, int slot, string name, long revisionBefore)
    {
        MutateSettings(id, revisionBefore, client =>
        {
            var before = ProcessingWire.PresetSlots(client.ReadPresetNames());
            if (mode != 3 && (slot < 1 || slot > before.Length)) throw new ArgumentOutOfRangeException(nameof(slot));
            if (mode == 2 && !before[slot-1].Occupied) throw new InvalidOperationException("Preset slot is empty.");
            var body = new byte[34];
            body[0] = mode;
            body[1] = (byte)Math.Max(0,slot-1);
            if (mode == 1) ProcessingWire.PutText(body,2,AmplifierService.NormalizeDeviceName(name),32);
            client.Write(59,body,messageStatus:0);
            var wait = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                Thread.Sleep(100);
                var slots = ProcessingWire.PresetSlots(client.ReadPresetNames());
                ReadSettings(client);
                lock (gate) presetSlots = slots;
                if (mode == 1 && slots.Length >= slot && slots[slot-1].Name == name ||
                    mode == 3 && slots.All(s => !s.Occupied) ||
                    mode == 2 && MatchesRecalledSlot(Text(native[2160..2192]), slot, before[slot-1].Name)) return;
            } while (wait.Elapsed.TotalSeconds < 3);
            throw new InvalidOperationException("The amplifier did not confirm the preset operation. Check the device before retrying.");
        });
        return new("Preset",true,"Device preset readback confirmed.");
    }

    private static bool MatchesRecalledSlot(string label, int slot, string name)
    {
        // DSP-1004 includes the one-based slot in the current preset label.
        return label == name || label == $"#{slot}.{name}" || label == $"#{slot:D2}.{name}";
    }
}

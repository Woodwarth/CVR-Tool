// Application / Observed live reads
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: expose full native settings and periodic status beside local devices.
// Units: native DSP units; telemetry timestamps are independent of settings age.
// State: one exclusive UDP owner, serialized polling and cached immutable reads.
// Scope: only evidenced write variants are enabled; every edit requires independent readback.
using System.Text;
using System.Text.Json;
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend : IAmplifierBackend, IAdvancedAmplifierBackend, INativePresetMetadataSource, IDisposable
{
    private readonly NetworkTarget target;
    private byte[]? identityBody;
    private readonly object gate = new();
    private readonly object ioGate = new();
    private readonly string id;
    private readonly Timer timer;
    private PresetDocument settings = null!;
    private byte[] native = [];
    private OutputTelemetry[] telemetry = [];
    private DeviceSnapshot device = null!;
    private DateTimeOffset settingsAt, telemetryAt;
    private double? power;
    private int[]? statusCodes;
    private string? error;
    private long revision;
    private int polling;
    private bool disposed;
    private bool forceSettingsRefresh;
    private bool standbyState;
    private bool standbyKnown;
    private OutputAllocation[]? allocation;
    private readonly Dictionary<int, FirState> firByChannel = new();
    private DevicePresetSlot[] presetSlots = [];
    private uint? operatingMinutes;
    private DateTimeOffset metadataCheckedAt;

    public CvrSnapshotBackend(NetworkTarget target)
    {
        id = $"cvr:{target.RemoteAddress}:{target.RemotePort}";
        this.target = target;
        CvrEndpointGate.UseClient(target, client =>
        {
            try { client.ReadSnapshot(0); } catch (TimeoutException) { }
            ReadSettings(client);
            ReadMetadata(client);
            ReadTelemetry(client);
            identityBody = client.ObservedIdentity;
            UpdateSnapshot();
        });
        timer = new Timer(_ => Poll(), null, 500, 500);
    }

    private static string Text(byte[] data) => Encoding.Latin1.GetString(data.TakeWhile(b => b != 0).ToArray()).Trim();
    private void CheckId(string value)
    {
        if (value != id) throw new ArgumentException("Unknown connected device.");
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    private void CheckChannel(int channel)
    {
        if (channel < 1 || channel > settings.Outputs.Count) throw new ArgumentOutOfRangeException(nameof(channel));
    }
    private void ReadSettings(CvrUdpClient client)
    {
        var bytes = client.ReadSnapshot(27);
        var parsed = NativePresetCodec.Decode(bytes);
        if (bytes[33] > 1) throw new NotSupportedException("Unknown device control flags.");
        lock (gate)
        {
            if (native.Length != 0 && (settings.Model != parsed.Model || settings.Outputs.Count != parsed.Outputs.Count))
                throw new NotSupportedException("Device identity/topology changed; reconnect explicitly.");
            if (native.Length != 0 && !native.SequenceEqual(bytes)) revision++;
            native = bytes;
            settings = parsed;
            settingsAt = DateTimeOffset.UtcNow;
            forceSettingsRefresh = false;
        }
    }
    private void ReadMetadata(CvrUdpClient client)
    {
        try
        {
            ReadWink(client);
            var body = client.ReadSnapshot(71);
            string header; lock (gate) header = Text(native[..32]);
            var minutes = DeviceMetadataCodec.OperatingMinutes(body, header);
            lock (gate) operatingMinutes = minutes;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or NotSupportedException) { }
        try
        {
            var names = ProcessingWire.PresetSlots(client.ReadPresetNames());
            lock (gate) presetSlots = names;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or NotSupportedException or System.Net.Sockets.SocketException)
        { lock (gate) presetSlots = []; }
        try
        {
            var raw = client.ReadAllocation();
            static double? Number(byte[] bytes, int offset) =>
                BitConverter.ToSingle(bytes, offset) is var v && float.IsFinite(v) ? v : null;
            lock (gate)
                allocation = Enumerable.Range(0, settings.Outputs.Count).Select(i =>
                    new OutputAllocation(raw[i], raw[4 + i], Number(raw, 8 + 4 * i), Number(raw, 24 + 4 * i))).ToArray();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or NotSupportedException or System.Net.Sockets.SocketException)
        { lock (gate) allocation = null; }
        finally { metadataCheckedAt = DateTimeOffset.UtcNow; }

        int count;
        lock (gate) count = settings.Outputs.Count;
        for (int channel = 1; channel <= count; channel++)
        {
            try
            {
                var raw = client.ReadInspection(43, (byte)(channel - 1), 0, 1);
                var fir = ProcessingWire.DecodeFir(channel, raw, false);
                lock (gate) firByChannel[channel] = fir;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or NotSupportedException or System.Net.Sockets.SocketException)
            { lock (gate) firByChannel.Remove(channel); }
        }
    }
    private void ReadTelemetry(CvrUdpClient client)
    {
        var bytes = client.ReadSnapshot(6);
        int count; lock (gate) count = settings.Outputs.Count;
        var readings = DecodeTelemetry(bytes, count);
        var reportedPower = bytes.Length == 32 ? (double?)null : ProcessingWire.Float(bytes, 16);
        var offset = bytes.Length == 32 ? 20 : 52;
        var codes = Enumerable.Range(0, count).Select(i => (int)bytes[offset + i]).ToArray();
        lock (gate)
        {
            for (int i = 0; i < count; i++)
            {
                var dynamics = settings.Dynamics![i];
                double reference = Math.Max(1, dynamics.Rms.MaximumVolts);
                var reading = readings[i];
                readings[i] = reading with
                {
                    OutputMeterDb = MeterDb(reading.Voltage, reference),
                    Status = reading.Status! with { HighImpedance = dynamics.LoadMode > 0, Bridged = dynamics.Bridged }
                };
            }
            telemetry = readings; power = reportedPower; statusCodes = codes;
            standbyKnown = codes.All(code => code <= 11);
            if (standbyKnown) standbyState = codes.All(code => code == 1);
            telemetryAt = DateTimeOffset.UtcNow;
        }
    }

    private static double? MeterDb(double? amplitude, double reference)
    {
        if (amplitude is not double value || !double.IsFinite(value) || !double.IsFinite(reference)) return null;
        return value <= 0 ? -40 : Math.Clamp(20 * Math.Log10(value / reference), -40, 0);
    }

    public static OutputTelemetry[] DecodeTelemetry(byte[] bytes, int count)
    {
        if (count is not (2 or 4) || (bytes.Length == 32 ? count != 2 : bytes.Length is not (56 or 72 or 76 or 88 or 92 or 96)))
            throw new NotSupportedException("Unsupported live telemetry layout.");
        return Enumerable.Range(0, count).Select(i =>
        {
            var compact = bytes.Length == 32;
            var voltage = ProcessingWire.Float(bytes, (compact ? 4 : 20) + i * 4);
            var current = ProcessingWire.Float(bytes, (compact ? 12 : 36) + i * 4);
            var code = bytes[(compact ? 20 : 52) + i];
            var flags = code <= 11 ? new OutputStatus(Fault: code is 2 or 6 or 7,
                Load: code == 4, Open: compact ? null : code == 3, OverTemperature: code == 9,
                Clip: code == 5, Standby: code == 1, Limiting: code == 10) : new OutputStatus();
            return new OutputTelemetry(i + 1, Voltage: voltage, Current: current,
                LoadOhms: current > 0 ? voltage / current : null,
                TemperatureC: compact ? null : ProcessingWire.Float(bytes, i * 4), Status: flags,
                InputMeterDb: bytes.Length is 72 or 88 or 92 or 96 ? MeterDb(BitConverter.ToSingle(bytes, 56 + i * 4), 1) : null,
                LimitDb: bytes.Length is 88 or 92 or 96 && float.IsFinite(BitConverter.ToSingle(bytes,72+i*4)) ? -BitConverter.ToSingle(bytes,72+i*4) : null,
                InputClip: bytes.Length is 92 or 96 ? bytes[88 + i] == 0 : null);
        }).ToArray();
    }
    private void UpdateSnapshot()
    {
        var identity = identityBody;
        var name = identity is not null ? Text(identity[32..64]) : "—";
        var mac = identity is not null ? string.Join(":", identity[64..70].Select(b => b.ToString("X2"))) : null;
        var currentPreset = native.Length is 2232 or 2687 ? Text(native[2160..2192]) : null;
        var readings = new DeviceReadings(true, Text(native[..32]), mac,
            string.IsNullOrEmpty(currentPreset) ? null : currentPreset, settingsAt,
            telemetryAt == default ? null : telemetryAt, power, statusCodes?.ToArray(), error, CanSetDisplayLock: true, CanSetStandby: standbyKnown, CanSetMutes: true, OperatingMinutes: operatingMinutes, CanSetWink: winkState is not null, CanSetInputGain: true, CanSetInputMute: settings.Inputs!.All(i => i.Muted is not null), CanRename: true, CanSetVerifiedDsp: true);
        device = new(id, string.IsNullOrWhiteSpace(name) ? "—" : name,
            new(settings.Model, settings.Outputs.Count, settings.Outputs.Count, false), false,
            settingsAt, settings.Outputs.ToArray(), readings);
    }
    private void Poll()
    {
        if (Interlocked.Exchange(ref polling, 1) != 0) return;
        try
        {
            lock (ioGate)
            {
                lock (gate) { if (disposed) return; }
                try
                {
                    DateTimeOffset observed; lock (gate) observed = settingsAt;
                    CvrEndpointGate.UseClient(target, client =>
                    {
                        if (forceSettingsRefresh || DateTimeOffset.UtcNow - observed >= TimeSpan.FromSeconds(5)) ReadSettings(client);
                        ReadTelemetry(client);
                        if (winkState is not null && DateTimeOffset.UtcNow - winkCheckedAt >= TimeSpan.FromSeconds(5)) ReadWink(client);
                        if (DateTimeOffset.UtcNow - metadataCheckedAt >= TimeSpan.FromMinutes(1)) ReadMetadata(client);
                        if (client.ObservedIdentity is { } identity) lock (gate) identityBody = identity;
                    });
                    lock (gate) { error = null; UpdateSnapshot(); }
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or NotSupportedException or System.Net.Sockets.SocketException)
                { lock (gate) { error = ex.Message; UpdateSnapshot(); } }
            }
        }
        finally { Volatile.Write(ref polling, 0); }
    }
    private DeviceSnapshot CopyDevice() => device with
    {
        Outputs = device.Outputs.ToArray(),
        Readings = device.Readings! with { StatusCodes = statusCodes?.ToArray() }
    };
    private static InputProcessingState CopyProcessing(InputProcessingState value) => value with { Bands = value.Bands.ToArray() };
    public IReadOnlyList<DeviceSnapshot> ListDevices() { lock (gate) return [CopyDevice()]; }
    public DeviceSnapshot GetDevice(string deviceId) { lock (gate) { CheckId(deviceId); return CopyDevice(); } }
    public (ChannelState State, long Revision) GetChannel(string deviceId, int channel)
    { lock (gate) { CheckId(deviceId); CheckChannel(channel); return (settings.Outputs.Single(c => c.Number == channel), revision); } }
    public IReadOnlyList<OutputTelemetry> GetTelemetry(string deviceId)
    { lock (gate) { CheckId(deviceId); return telemetry.ToArray(); } }
    public RoutingSnapshot GetRouting(string deviceId)
    { lock (gate) { CheckId(deviceId); return new(id, settings.Outputs.Count, settings.Outputs.Count, settings.Routing!.ToArray(), revision); } }
    public (InputProcessingState State, long Revision) GetInputProcessing(string deviceId, int channel)
    { lock (gate) { CheckId(deviceId); CheckChannel(channel); return (CopyProcessing(settings.Inputs!.Single(c => c.Channel == channel)), revision); } }
    public (InputProcessingState State, long Revision) GetOutputProcessing(string deviceId, int channel)
    { lock (gate) { CheckId(deviceId); CheckChannel(channel); return (CopyProcessing(settings.OutputProcessing!.Single(c => c.Channel == channel)), revision); } }
    public (OutputDynamicsState State, long Revision) GetOutputDynamics(string deviceId, int channel)
    { lock (gate) { CheckId(deviceId); CheckChannel(channel); return (settings.Dynamics!.Single(c => c.Channel == channel), revision); } }
    public (FirState State, long Revision) GetFir(string deviceId, int channel)
    {
        lock (gate)
        {
            CheckId(deviceId);
            CheckChannel(channel);
            bool enabled = native[36 + (channel - 1) * 515 + 368] == 0;
            return (firByChannel.TryGetValue(channel, out var fir)
                ? fir with { Enabled = enabled, Coefficients = fir.Coefficients.ToArray() }
                : new(channel, "Unknown", [], enabled, CoefficientsKnown: false), revision);
        }
    }
    public DeviceControlState GetControlState(string deviceId)
    { lock (gate) { CheckId(deviceId); return new(native[33] != 0, standbyState, StandbyKnown: standbyKnown, Wink: winkState); } }
    public DevicePresetState GetDevicePresets(string deviceId)
    { lock (gate) { CheckId(deviceId); return new(presetSlots.ToArray(), device.Readings?.CurrentPreset); } }
    public NativePresetMetadata GetNativePresetMetadata(string deviceId)
    { lock (gate) { CheckId(deviceId); return NativePresetMetadata.Decode(native) with { Allocation = allocation?.ToArray() }; } }
    public PresetDocument CapturePreset(string deviceId)
    { lock (gate) { CheckId(deviceId); return JsonSerializer.Deserialize<PresetDocument>(JsonSerializer.Serialize(settings with { Name = device.Name }))!; } }

    private static NotSupportedException ReadOnly() => new("Only physically evidenced command variants are writable on this live connection.");
    public InputProcessingState ApplyOutputProcessing(InputProcessingChange change, long expectedRevision)
    {
        if (change.DelayMs.HasValue || change.GainDb.HasValue || change.Muted.HasValue)
            throw new NotSupportedException("Use the output channel controls for gain, mute and delay.");
        MutateSettings(change.DeviceId, expectedRevision, client =>
        {
            CheckChannel(change.Channel);
            var index = change.Channel - 1;
            var offset = 36 + index * 515 + 226;
            var before = settings.OutputProcessing![index];
            var after = AmplifierService.MergeInput(before, change);
            var block = ProcessingWire.EncodeEq(after, native[offset..(offset + 141)]);
            if (SupportsCombinedEq)
            {
                foreach (var band in after.Bands)
                {
                    int position = band.Number * 14 + 1;
                    var prior = before.Bands.Single(b => b.Number == band.Number);
                    if (band.FilterType != 0 || prior.GainDb == band.GainDb || prior.FrequencyHz == band.FrequencyHz) continue;
                    // Use the point command only when it covers the entire pending edit.
                    if (block.AsSpan(0, position).SequenceEqual(native.AsSpan(offset, position)) &&
                        block.AsSpan(position + 8).SequenceEqual(native.AsSpan(offset + position + 8, block.Length - position - 8)))
                    {
                        WriteEqPoint(client, (byte)index, 1, (byte)band.Number, offset + position, band);
                        return;
                    }
                }
            }
            WriteSettingsBytes(client, 52, (byte)index, 1, offset, block, messageStatus: 0);
        });
        return GetOutputProcessing(change.DeviceId, change.Channel).State;
    }
    public FirState ApplyFir(string deviceId, FirState state, long expectedRevision)
    {
        state = AmplifierService.ValidateFir(state);
        MutateSettings(deviceId, expectedRevision, client =>
        {
            CheckChannel(state.Channel);
            byte ch = (byte)(state.Channel - 1);
            var prior = client.ReadInspection(43, ch, 0, 1);
            var bytes = ProcessingWire.EncodeFir(state, prior.Length == 2080);
            if (!bytes.SequenceEqual(prior))
            {
                client.Write(43, bytes, ch, side: 1);
                var confirmed = client.ReadInspection(43, ch, 0, 1);
                lock (gate) firByChannel[state.Channel] = ProcessingWire.DecodeFir(state.Channel, confirmed, false);
                if (!confirmed.SequenceEqual(bytes))
                    throw new InvalidOperationException("The amplifier did not confirm the new FIR coefficients.");
                lock (gate) revision++;
            }
            WriteSettingsBytes(client, 44, ch, 1, 36 + ch * 515 + 368, [(byte)(state.Enabled ? 0 : 1)]);
        });
        return GetFir(deviceId, state.Channel).State;
    }
    public DeviceOperationResult FactoryReset(string deviceId, long expectedRevision)
    {
        MutateSettings(deviceId, expectedRevision, client =>
        {
            lock (gate) { firByChannel.Clear(); presetSlots = []; }
            client.ResetSnapshot();
            ReadSettings(client);
            ReadMetadata(client);
            ReadTelemetry(client);
        });
        return new("Factory reset", true, "Reset settings received and read back.");
    }
    public void Dispose()
    {
        timer.Dispose();
        lock (ioGate)
        {
            lock (gate) disposed = true;
        }
    }
}

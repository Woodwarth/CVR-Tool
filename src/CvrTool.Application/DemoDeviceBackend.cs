// Application / Demo devices — names, lock/standby and simulated input and output meters; no sockets.
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
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
            SaveList(devices.Values.Select(e => e.Device.Id == deviceId ? result : e.Device));
            devices[deviceId] = (result, entry.Revision + 1);
            return Copy(result);
        }
    }

    public DeviceControlState SetWink(string deviceId, bool enabled, long expectedRevision)
    {
        lock (gate)
        {
            var entry = Find(deviceId);
            if (entry.Revision != expectedRevision) throw new InvalidOperationException("Device changed. Try Wink again.");
            var after = GetControlState(deviceId) with { Wink = enabled };
            controls[deviceId] = after;
            devices[deviceId] = (entry.Device with { ObservedAt = DateTimeOffset.UtcNow }, entry.Revision + 1);
            return after;
        }
    }

    private readonly Dictionary<string, DeviceControlState> controls = new();
    public DeviceControlState GetControlState(string deviceId)
    {
        lock (gate)
        {
            Find(deviceId);
            return controls.GetValueOrDefault(deviceId) ?? new(Wink: false);
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

    private readonly Dictionary<(string Device, int Channel), (double Celsius, double Time)> temperatures = new();

    public IReadOnlyList<OutputTelemetry> GetTelemetry(string deviceId)
    {
        lock (gate)
        {
            var seconds = Environment.TickCount64 / 1000.0;
            var standby = GetControlState(deviceId).Standby == true;
            var inputs = FindInputs(deviceId);
            var levels = new double[inputs.Length];
            foreach (var input in inputs)
            {
                var phase = seconds * 1.8 + input.Channel * 1.6;
                var level = -18 + 12 * Math.Sin(phase) + 8 * Math.Sin(phase * 0.41) + (input.GainDb ?? 0);
                levels[input.Channel - 1] = standby || input.Muted == true ? double.NegativeInfinity : Math.Min(0, level);
            }

            // Sum independent input powers before applying output trim.
            var power = new double[inputs.Length];
            foreach (var route in routing[deviceId])
                if (!route.Muted)
                    power[route.Output - 1] += Math.Pow(10, (levels[route.Input - 1] + route.GainDb) / 10);

            var outputs = Find(deviceId).Device.Outputs;
            var metadata = GetNativePresetMetadata(deviceId);
            return outputs.Select(output =>
            {
                var i = output.Number - 1;
                var settings = GetOutputDynamics(deviceId, output.Number).State;
                var source = settings.Bridged ? i - i % 2 : i;
                var channel = outputs[source];
                if (source != i) settings = GetOutputDynamics(deviceId, channel.Number).State;
                var db = channel.Muted ? double.NegativeInfinity : 10 * Math.Log10(power[source]) + channel.GainDb;
                if (settings.NoiseGateEnabled && db < settings.NoiseGateDb) db = double.NegativeInfinity;
                double reference = settings.LoadMode switch { 1 => 70, 2 => 100, _ => settings.Rms.MaximumVolts };
                var requested = reference * Math.Pow(10, db / 20);
                var ceiling = reference;
                if (settings.Rms.Enabled) ceiling = Math.Min(ceiling, settings.Rms.ThresholdVolts);
                if (settings.Peak.Enabled) ceiling = Math.Min(ceiling, settings.Peak.ThresholdVolts / Math.Sqrt(2));
                var volts = Math.Min(requested, ceiling);
                var limiting = ceiling < reference && requested > ceiling;
                var meter = Math.Clamp(20 * Math.Log10(volts / reference), -60, 0);
                var configuredLoad = metadata?.Channels.FirstOrDefault(c => c.Channel == channel.Number)?.ConfiguredLoadOhms;
                var load = configuredLoad is > 0 ? configuredLoad.Value : 8;
                var current = volts / load;
                var key = (deviceId, output.Number);
                var previous = temperatures.GetValueOrDefault(key, (Celsius: 30.0, Time: seconds));
                var target = standby ? 23 : 30 + Math.Min(65, volts * current / 16);
                var temperature = target + (previous.Celsius - target) * Math.Exp(-(seconds - previous.Time) / 18);
                temperatures[key] = (temperature, seconds);
                var status = new OutputStatus(Fault: false, Load: load < 2, Open: load > 10000,
                    OverTemperature: temperature >= 80, Clip: db >= 0 && !limiting, Standby: standby,
                    HighImpedance: settings.LoadMode > 0, Bridged: settings.Bridged, Limiting: limiting);
                var reduction = limiting ? Math.Clamp(20 * Math.Log10(volts / requested), -60, 0) : 0;
                return new OutputTelemetry(output.Number, meter, volts, current, load, temperature, status,
                    OutputMeterDb: meter, InputMeterDb: Math.Clamp(levels[i], -60, 0), InputClip: levels[i] >= 0,
                    LimitDb: reduction);
            }).ToArray();
        }
    }
}

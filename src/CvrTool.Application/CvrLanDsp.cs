// Application / CVR input DSP and telemetry
// Purpose: map bounded wire values into immutable application snapshots.
// Units: ms, Hz, dB, Q, volts, amperes, ohms and degrees Celsius.
// State: preserve unedited filter types, crossover records and reset bytes.
// Scope: unsupported telemetry remains unknown; voltage is never treated as dBFS.
using CvrTool.Core;

namespace CvrTool.Application;

public sealed partial class CvrLanBackend
{
    private (InputProcessingState State, byte[] Raw) ReadInput(int channel)
    {
        var ch = Channel(channel, true);
        var delay = Float(Read(14, ch));
        var raw = Read(52, ch);
        if (raw.Length != 141 || raw[140] > 1)
            throw new NotSupportedException("Unsupported CVR input filter format.");
        var bands = Enumerable.Range(1, 8).Select(n => new EqualizerBand(n, Float(raw, n * 14 + 5), Float(raw, n * 14 + 1), Float(raw, n * 14 + 9), unchecked((sbyte)raw[n * 14]) >= 0, raw[n * 14] < 128 ? raw[n * 14] : 255 - raw[n * 14])).ToArray();
        CrossoverFilter Crossover(int offset) => new(Float(raw, offset + 5), raw[offset] < 128 ? raw[offset] : 255 - raw[offset], raw[offset] < 128);
        return (new(channel, delay, raw[140] == 0, bands, Crossover(0), Crossover(126)), raw);
    }
    public (InputProcessingState State, long Revision) GetInputProcessing(string deviceId, int channel)
    {
        lock (gate)
        {
            CheckId(deviceId);
            var input = ReadInput(channel);
            return (input.State, revision);
        }
    }
    public InputProcessingState ApplyInputProcessing(InputProcessingChange change, long expectedRevision)
    {
        lock (gate)
        {
            CheckId(change.DeviceId);
            var before = ReadInput(change.Channel);
            Expected(expectedRevision);
            var after = AmplifierService.MergeInput(before.State, change);
            var ch = Channel(change.Channel, true);
            if (change.DelayMs is not null)
                Write(14, FloatBytes(after.DelayMs), ch);
            if (change.Bands is not null || change.EqEnabled is not null || change.HighPass is not null || change.LowPass is not null)
            {
                var raw = before.Raw.ToArray();
                raw[140] = (byte)(after.EqEnabled ? 0 : 1);
                if (change.Bands is not null)
                    foreach (var band in after.Bands)
                    {
                        int offset = band.Number * 14;
                        int signed = unchecked((sbyte)raw[offset]);
                        int type = signed < 0 ? -signed - 1 : signed;
                        raw[offset] = unchecked((byte)(band.Enabled ? type : -type - 1));
                        FloatBytes(band.GainDb).CopyTo(raw, offset + 1);
                        FloatBytes(band.FrequencyHz).CopyTo(raw, offset + 5);
                        FloatBytes(band.Q).CopyTo(raw, offset + 9);
                    }
                void Crossover(int offset, CrossoverFilter? filter)
                {
                    if (filter is null)
                        return;
                    raw[offset] = unchecked((byte)(filter.Enabled ? filter.FilterType : -filter.FilterType - 1));
                    FloatBytes(filter.FrequencyHz).CopyTo(raw, offset + 5);
                }
                Crossover(0, change.HighPass);
                Crossover(126, change.LowPass);
                Write(52, raw, ch);
            }
            return ReadInput(change.Channel).State;
        }
    }
    public IReadOnlyList<OutputTelemetry> GetTelemetry(string deviceId)
    {
        lock (gate)
        {
            CheckId(deviceId);
            var body = client.Read(6);
            return Enumerable.Range(1, capabilities.OutputCount).Select(channel =>
            {
                int i = channel - 1;
                double? voltage = null, current = null, temperature = null;
                byte? status = null;
                if (body.Length is 56 or 72 or 76 or 88 or 92 or 96)
                {
                    temperature = Float(body, i * 4);
                    voltage = Float(body, 20 + i * 4);
                    current = Float(body, 36 + i * 4);
                    status = body[52 + i];
                }
                else if (body.Length == 32 && capabilities.OutputCount == 2)
                {
                    voltage = Float(body, 4 + i * 4);
                    current = Float(body, 12 + i * 4);
                    status = body[20 + i];
                }
                var flags = status is { } value && value <= 11
                    ? new OutputStatus(Fault: value is 2 or 6 or 7, Load: value == 4, Open: body.Length != 32 ? value == 3 : null, OverTemperature: value == 9, Clip: value == 5, Standby: value == 1)
                    : new OutputStatus();
                return new OutputTelemetry(channel, Voltage: voltage, Current: current, LoadOhms: current > 0 ? voltage / current : null, TemperatureC: temperature, Status: flags);
            }).ToArray();
        }
    }
}

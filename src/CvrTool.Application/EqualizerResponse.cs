// Application / Parametric EQ response
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: calculate a graphical preview using RBJ peaking biquads.
// Units: Hz, dB and Q; preview sample rate is explicitly 48 kHz.
// State: this calculation never changes amplifier settings.
// Scope: unknown device filter types are not approximated as peak filters.
using System.Numerics;
using CvrTool.Core;

namespace CvrTool.Application;

public static class EqualizerResponse
{
    public const double SampleRate = 48000;
    // Invert the complex EQ response, then window 512 samples around time zero.
    public static double[] GenerateFir(IReadOnlyList<EqualizerBand> bands, CrossoverFilter? high,
        CrossoverFilter? low, bool enabled)
    {
        const int length = 512, bins = 4096;
        var spectrum = new Complex[bins / 2 + 1];
        for (int k = 0; k < spectrum.Length; k++)
        {
            double hz = Math.Clamp(k * SampleRate / bins, 0.00001, SampleRate / 2 - 0.00001);
            var value = Complex.One;
            if (enabled)
            {
                value = CrossoverResponse.Transfer(high, true, hz) * CrossoverResponse.Transfer(low, false, hz);
                foreach (var band in bands) value *= Transfer(band, hz);
            }
            spectrum[k] = value;
        }
        var taps = new double[length];
        for (int n = 0; n < length; n++)
        {
            int lag = n - length / 2;
            var step = Complex.FromPolarCoordinates(1, 2 * Math.PI * lag / bins);
            var rotation = step;
            double sum = spectrum[0].Real + ((lag & 1) == 0 ? spectrum[^1].Real : -spectrum[^1].Real);
            for (int k = 1; k < spectrum.Length - 1; k++, rotation *= step)
                sum += 2 * (spectrum[k] * rotation).Real;
            taps[n] = sum / bins * (0.5 - 0.5 * Math.Cos(2 * Math.PI * n / length));
            if (!double.IsFinite(taps[n])) throw new InvalidDataException("Cannot generate FIR for this EQ.");
        }
        return taps;
    }

    public static double GainDb(EqualizerBand band, double frequency) => 20 * Math.Log10(Transfer(band, frequency).Magnitude);

    public static Complex Transfer(EqualizerBand band, double frequency)
    {
        if (!band.Enabled)
            return Complex.One;
        if (band.FilterType != 0)
            throw new NotSupportedException("Curve unavailable for this device filter type.");
        var amplitude = Math.Pow(10, band.GainDb / 40);
        var omega = 2 * Math.PI * band.FrequencyHz / SampleRate;
        var alpha = Math.Sin(omega) / (2 * band.Q);
        var cosine = Math.Cos(omega);
        var z = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / SampleRate);
        var numerator = 1 + alpha * amplitude - 2 * cosine * z + (1 - alpha * amplitude) * z * z;
        var denominator = 1 + alpha / amplitude - 2 * cosine * z + (1 - alpha / amplitude) * z * z;
        return numerator / denominator;
    }
}

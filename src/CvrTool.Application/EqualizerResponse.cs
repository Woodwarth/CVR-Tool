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

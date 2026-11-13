// Application / Crossover preview
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: preview Butterworth, phase-normalized Bessel and Linkwitz-Riley responses.
// Units: Hz and dB; bilinear frequency warping at the explicit 48 kHz preview rate.
// State: independent mathematical preview, never device coefficients or writes.
// Scope: firmware response/normalization is not established by the preview.
using System.Numerics;
using CvrTool.Core;

namespace CvrTool.Application;

public static class CrossoverResponse
{
    public static double GainDb(CrossoverFilter? filter, bool highPass, double frequency) => 20 * Math.Log10(Transfer(filter, highPass, frequency).Magnitude);

    public static Complex Transfer(CrossoverFilter? filter, bool highPass, double frequency)
    {
        if (filter is null || !filter.Enabled)
            return Complex.One;
        var option = CrossoverFilters.Options.SingleOrDefault(o => o.Code == filter.FilterType) ?? throw new NotSupportedException("Unknown crossover type.");
        var ratio = Math.Tan(Math.PI * frequency / EqualizerResponse.SampleRate) / Math.Tan(Math.PI * filter.FrequencyHz / EqualizerResponse.SampleRate);
        if (highPass)
            ratio = -1 / ratio;
        var order = option.Slope / 6;
        if (option.Family is "Butterworth" or "Linkwitz-Riley")
        {
            var stages = option.Family == "Linkwitz-Riley" ? order / 2 : order;
            var denominator = Complex.One;
            for (var k = 0; k < stages; k++)
            {
                var pole = Complex.FromPolarCoordinates(1, Math.PI * (2 * k + stages + 1) / (2 * stages));
                denominator *= new Complex(0, ratio) - pole;
            }
            return option.Family == "Linkwitz-Riley" ? 1 / (denominator * denominator) : 1 / denominator;
        }
        // Reverse Bessel polynomial: (2n-k)! / (2^(n-k) k! (n-k)!).
        // Phase normalization matches the Butterworth asymptote at the selected cutoff.
        static double Factorial(int n)
        {
            double result = 1;
            for (var i = 2; i <= n; i++)
                result *= i;
            return result;
        }
        var constant = Factorial(2 * order) / (Math.Pow(2, order) * Factorial(order));
        var s = new Complex(0, ratio * Math.Pow(constant, 1.0 / order));
        var polynomial = Complex.Zero;
        for (var k = order; k >= 0; k--)
        {
            var coefficient = Factorial(2 * order - k) / (Math.Pow(2, order - k) * Factorial(k) * Factorial(order - k));
            polynomial = polynomial * s + coefficient;
        }
        return constant / polynomial;
    }
}

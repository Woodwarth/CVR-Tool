// Application / Crossover preview
// Purpose: preview Butterworth, phase-normalized Bessel and Linkwitz-Riley responses.
// Units: Hz and dB; bilinear frequency warping at the explicit 48 kHz preview rate.
// State: independent mathematical preview, never device coefficients or writes.
// Scope: firmware response/normalization is not established by the preview.
using System.Numerics;
using CvrTool.Core;

namespace CvrTool.Application;

public static class CrossoverResponse
{
    public static double GainDb(CrossoverFilter? filter, bool highPass, double frequency)
    {
        if (filter is null || !filter.Enabled)
            return 0;
        var option = CrossoverFilters.Options.SingleOrDefault(o => o.Code == filter.FilterType) ?? throw new NotSupportedException("Unknown crossover type.");
        var ratio = Math.Tan(Math.PI * frequency / EqualizerResponse.SampleRate) / Math.Tan(Math.PI * filter.FrequencyHz / EqualizerResponse.SampleRate);
        if (highPass)
            ratio = 1 / ratio;
        var order = option.Slope / 6;
        if (option.Family == "Butterworth")
            return -10 * Math.Log10(1 + Math.Pow(ratio, 2 * order));
        if (option.Family == "Linkwitz-Riley")
            return -20 * Math.Log10(1 + Math.Pow(ratio, order));
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
        return 20 * Math.Log10(constant / polynomial.Magnitude);
    }
}

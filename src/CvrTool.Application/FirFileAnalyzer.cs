// Application / FIR analysis — parse coefficients and calculate a read-only DSP preview.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using System.Globalization;

namespace CvrTool.Application;

public sealed record FirResponsePoint(double FrequencyHz, double MagnitudeDb, double PhaseDegrees);
public sealed record FirFileAnalysis(int SampleRate, IReadOnlyList<double> Coefficients, IReadOnlyList<FirResponsePoint> Response)
{
    public int NonZeroCount => Coefficients.Count(c => c != 0);
    public bool CanApply => false;
}

/// <summary>Offline FIR text analysis; sample rate is an explicit preview setting.</summary>
public static class FirFileAnalyzer
{
    public const int MaximumCharacters = 1024 * 1024;
    public const int MaximumCoefficients = 65536;
    public static FirFileAnalysis Analyze(string text, int sampleRate = 48000)
    {
        if (text.Length > MaximumCharacters)
            throw new InvalidDataException("FIR text exceeds 1 MiB.");
        if (sampleRate is < 8000 or > 192000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var coefficients = new List<double>();
        using var reader = new StringReader(text);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            line = line.Trim().TrimStart('\uFEFF');
            if (line.Length == 0)
                continue;
            if (!double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || Math.Abs(value) > 1e6)
                throw new InvalidDataException($"Invalid FIR coefficient on line {lineNumber}. Expected one finite number per line with a decimal point.");
            if (coefficients.Count == MaximumCoefficients)
                throw new InvalidDataException("Local preview supports up to 65,536 coefficients.");
            coefficients.Add(value);
        }

        if (coefficients.Count == 0)
            throw new InvalidDataException("The FIR file contains no coefficients.");
        var response = new List<FirResponsePoint>();
        var upperFrequency = Math.Min(20000, sampleRate / 2.0);
        for (var i = 0; i <= 256; i++)
        {
            var frequency = 20 * Math.Pow(upperFrequency / 20, i / 256.0);
            var angle = -2 * Math.PI * frequency / sampleRate;
            var (stepSin, stepCos) = Math.SinCos(angle);
            double real = 0, imaginary = 0, cosine = 1, sine = 0;
            foreach (var coefficient in coefficients)
            {
                real += coefficient * cosine;
                imaginary += coefficient * sine;
                (cosine, sine) = (cosine * stepCos - sine * stepSin, sine * stepCos + cosine * stepSin);
            }

            var magnitude = Math.Sqrt(real * real + imaginary * imaginary);
            response.Add(new(frequency, magnitude == 0 ? double.NegativeInfinity : 20 * Math.Log10(magnitude), magnitude == 0 ? double.NaN : Math.Atan2(imaginary, real) * 180 / Math.PI));
        }

        return new(sampleRate, coefficients.AsReadOnly(), response.AsReadOnly());
    }

    public static async Task<FirFileAnalysis> AnalyzeAsync(Stream stream, int sampleRate = 48000, CancellationToken cancellation = default)
    {
        using var textReader = new StreamReader(stream, leaveOpen: true);
        var text = new System.Text.StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await textReader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false);
            if (count == 0)
                break;
            if (text.Length + count > MaximumCharacters)
                throw new InvalidDataException("FIR text exceeds 1 MiB.");
            text.Append(buffer, 0, count);
        }

        return Analyze(text.ToString(), sampleRate);
    }
}

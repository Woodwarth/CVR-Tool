// Core / Crossover filters
// Module: Defines device models, channel state and the contracts shared by all backends.
// Purpose: describe input high-/low-pass settings and the eleven CVR selections.
// Units: cutoff Hz and nominal dB/octave slope.
// State: immutable values; bypass preserves the selected frequency and type.
// Scope: type IDs follow observed controller wire values, not screenshot guesses.
namespace CvrTool.Core;

public sealed record CrossoverFilter(double FrequencyHz, int FilterType = 0, bool Enabled = false);
public sealed record CrossoverOption(int Code, int Slope, string Family, string ShortName)
{
    public string Label => $"{Slope} dB {Family}";
}
public static class CrossoverFilters
{
    public static IReadOnlyList<CrossoverOption> Options
    {
        get;
    } = Array.AsReadOnly(new[]
    {
        new CrossoverOption(0,12,"Butterworth","BW-12"),new(1,12,"Bessel","BE-12"),new(2,12,"Linkwitz-Riley","LR-12"),
        new(3,18,"Butterworth","BW-18"),new(4,24,"Butterworth","BW-24"),new(5,24,"Bessel","BE-24"),new(6,24,"Linkwitz-Riley","LR-24"),
        new(7,36,"Butterworth","BW-36"),new(8,48,"Butterworth","BW-48"),new(9,48,"Bessel","BE-48"),new(10,48,"Linkwitz-Riley","LR-48")
    });
}

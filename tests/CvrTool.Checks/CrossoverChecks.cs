// Checks / Crossovers
// Purpose: verify shared edits, supported selections and analytical response landmarks.
// Input: synthetic two-channel session; no amplifier access.
// State: previews, apply, bypass and revisions share the regular input lifecycle.
// Scope: mathematical preview checks do not establish device response accuracy.
using CvrTool.Application;
using CvrTool.Core;

internal static class CrossoverChecks
{
    public static void Run(Action<bool, string> check)
    {
        var service = new AmplifierService(new DemoBackend());
        const string id = "demo-2002";
        check(CrossoverFilters.Options.Count == 11 && CrossoverFilters.Options.Select(o => o.ShortName).Distinct().Count() == 11, "Eleven distinct crossover types match the reference selection");
        var before = service.GetInputProcessing(id, 1);
        check(before.HighPass is { Enabled: false, FrequencyHz: 20 } && before.LowPass is { Enabled: false, FrequencyHz: 20000 }, "Local crossover defaults are bypassed");
        var stale = service.Plan(new(id, 1, GainDb: -3));
        foreach (var option in CrossoverFilters.Options)
        {
            var high = new CrossoverFilter(1000, option.Code, true);
            var low = new CrossoverFilter(15000, option.Code, false);
            var plan = service.PlanInputProcessing(new(id, 1, HighPass: high, LowPass: low));
            check(service.GetInputProcessing(id, 1).HighPass == before.HighPass, "Crossover preview performs no writes: " + option.ShortName);
            var applied = service.ApplyInputProcessing(plan.Id);
            check(applied.HighPass == high && applied.LowPass == low && applied.Bands.SequenceEqual(before.Bands), "Crossover apply preserves eight EQ bands: " + option.ShortName);
            before = applied;
            check(CrossoverResponse.GainDb(high, true, 100) > CrossoverResponse.GainDb(high, true, 20) && CrossoverResponse.GainDb(high, true, 10000) > CrossoverResponse.GainDb(high, true, 100), "High-pass response is monotonic: " + option.ShortName);
            check(CrossoverResponse.GainDb(high, false, 100) > CrossoverResponse.GainDb(high, false, 10000), "Low-pass response attenuates the upper stopband: " + option.ShortName);
        }
        try
        {
            service.Apply(stale.Id);
            throw new Exception("Stale channel plan accepted");
        }
        catch (InvalidOperationException) { check(true, "Crossover writes invalidate older channel plans"); }
        foreach (var filter in new[] { new CrossoverFilter(double.NaN), new(19), new(20001), new(80, -1), new(80, 11) })
        {
            try
            {
                service.PlanInputProcessing(new(id, 1, HighPass: filter));
                throw new Exception("Invalid crossover accepted");
            }
            catch (ArgumentOutOfRangeException) { check(true, "Invalid crossover cutoff/type rejected"); }
        }
        check(Math.Abs(CrossoverResponse.GainDb(new(1000, 4, true), false, 1000) + 3.010299956639812) < 1e-9, "Butterworth cutoff is -3.0103 dB");
        check(Math.Abs(CrossoverResponse.GainDb(new(1000, 6, true), true, 1000) + 6.020599913279624) < 1e-9, "Linkwitz-Riley cutoff is -6.0206 dB");
        check(Math.Abs(CrossoverResponse.GainDb(new(1000, 1, true), false, 1000) + 4.771212547196624) < 1e-9, "Second-order phase-normalized Bessel cutoff matches its analytic polynomial");
        check(CrossoverResponse.GainDb(new(80, 9, false), true, 20) == 0, "Bypassed crossover has unity response");
        check(service.GetInputProcessing(id, 2).HighPass is { Enabled: false }, "Crossover edits remain isolated to the selected input");
    }
}

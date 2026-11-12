// Application / Evidenced input crossovers
// Purpose: share the limited live HP/LP type policy between backend and UI.
// Units: controller type IDs; BW-12 is 0 and BW-24 is 4.
// State: based on controlled Input 1 HP/LP captures, not model-wide certification.
// Scope: other crossover types remain protected until independently evidenced.
using CvrTool.Core;

namespace CvrTool.Application;

public static class ObservedInputCrossovers
{
    public static bool CanWrite(CrossoverFilter? filter) => filter?.FilterType is 0 or 4;
    public static IReadOnlyList<CrossoverOption> Options { get; } =
        Array.AsReadOnly(CrossoverFilters.Options.Where(o => o.Code is 0 or 4).ToArray());
}

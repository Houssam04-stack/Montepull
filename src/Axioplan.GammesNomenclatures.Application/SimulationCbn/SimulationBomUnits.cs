namespace Axioplan.GammesNomenclatures.Application.SimulationCbn;

/// <summary>
/// Unites autorisees pour les composants de nomenclature simulation.
/// </summary>
public static class SimulationBomUnits
{
    public const string Min = "min";
    public const string Kg = "kg";
    public const string Un = "un";

    public static readonly IReadOnlyList<string> All = [Min, Kg, Un];

    public static string Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return Un;
        }

        var value = unit.Trim().ToLowerInvariant();
        return value switch
        {
            Min => Min,
            Kg => Kg,
            Un or "pcs" or "u" or "unit" or "piece" or "pce" => Un,
            _ => All.Contains(value) ? value : Un
        };
    }
}

namespace Axioplan.GammesNomenclatures.Application.SimulationCbn;

/// <summary>
/// Types de nomenclature simulation (produit de base, achat, production).
/// </summary>
public static class SimulationNomenclatureTypes
{
    public const string Base = "BASE";
    public const string Achat = "ACHAT";
    public const string Production = "PRODUCTION";

    public const int DefaultAlternativeAchat = 10;
    public const int DefaultAlternativeProduction = 20;

    public static bool IsKnown(string? value)
        => string.Equals(value, Base, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Achat, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Production, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Base;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            Base => Base,
            "ACHAT" => Achat,
            "PRODUCTION" or "PROD" => Production,
            _ => Base
        };
    }
}

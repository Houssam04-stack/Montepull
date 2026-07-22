namespace Axioplan.GammesNomenclatures.Domain;

/// <summary>
/// Port fidèle de src/axioplan/modules/gammes_nomenclatures/generation.py
/// </summary>
public static class GenerationEngine
{
    public static IReadOnlyList<GenerationVariant> BuildSizeColorVariants(
        string baseArticleCode,
        IReadOnlyList<string> sizes,
        IReadOnlyList<string> colors)
    {
        var variants = new List<GenerationVariant>();

        foreach (var size in sizes)
        {
            foreach (var color in colors)
            {
                variants.Add(new GenerationVariant(
                    $"{baseArticleCode}-{size}-{color}",
                    size,
                    color));
            }
        }

        return variants;
    }

    public static double CalculateNetQuantity(
        double baseQuantity,
        double sizeCoefficient = 1.0,
        double colorCoefficient = 1.0)
    {
        return baseQuantity * sizeCoefficient * colorCoefficient;
    }

    public static double CalculateNetQuantityFromFormula(
        double baseQuantity,
        string? formulaExpression,
        IReadOnlyDictionary<string, double> argumentCoefficients)
    {
        if (string.IsNullOrWhiteSpace(formulaExpression))
        {
            var size = argumentCoefficients.TryGetValue("SIZE", out var sizeValue) ? sizeValue : 1.0;
            var color = argumentCoefficients.TryGetValue("COLOR", out var colorValue) ? colorValue : 1.0;
            return CalculateNetQuantity(baseQuantity, size, color);
        }

        return FormulaEngine.Evaluate(formulaExpression, baseQuantity, argumentCoefficients);
    }

    public static string BuildFormulaTrace(
        string? formulaExpression,
        double baseQuantity,
        IReadOnlyDictionary<string, double> argumentCoefficients,
        bool applyOrderQuantity,
        double orderQuantity,
        double lossRate)
    {
        var display = string.IsNullOrWhiteSpace(formulaExpression)
            ? "BesoinBase * SIZE * COLOR"
            : formulaExpression;

        var unitNetTrace = display;
        if (lossRate > 0)
        {
            unitNetTrace = $"({display}) / (1 - {lossRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
        }

        if (!applyOrderQuantity)
        {
            return $"besoin_brut_unitaire = besoin_net_unitaire / (1 - perte) => {unitNetTrace}";
        }

        return lossRate > 0
            ? $"besoin_brut_unitaire = besoin_net_unitaire / (1 - perte) ; besoin_brut_total = besoin_net_total / (1 - perte) => ({display}) * order_qty / (1 - {lossRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})"
            : $"besoin_brut_total = besoin_net_total => ({display}) * order_qty";
    }

    public static double CalculateGrossQuantity(double netQuantity, double lossRate = 0.0)
    {
        if (lossRate >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lossRate), "loss_rate must be lower than 1");
        }

        return netQuantity / (1 - lossRate);
    }

    public static double CalculateOperationTime(
        double baseTime,
        double sizeCoefficient = 1.0,
        double colorCoefficient = 1.0)
    {
        return baseTime * sizeCoefficient * colorCoefficient;
    }

    public static bool ProfileCanGenerate(string profileStatus)
    {
        return profileStatus == "VALIDATED";
    }
}

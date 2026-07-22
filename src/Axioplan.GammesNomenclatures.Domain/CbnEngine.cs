namespace Axioplan.GammesNomenclatures.Domain;

public static class CbnEngine
{
    public sealed record FlattenedBomLine(
        int LineNo,
        string ComponentCode,
        double QuantityBase,
        string Unit,
        double LossRate,
        string Behavior);

    public sealed record MaterialRequirementDraft(
        string ComponentCode,
        string VariantLabel,
        string CalculationMode,
        double QuantityNet,
        double QuantityGross,
        string Unit,
        string SourcePath,
        CalculationTraceDraft Trace);

    public sealed record CalculationTraceDraft(
        string ObjectType,
        string ComponentCode,
        string CalculationMode,
        string RuleCode,
        double BaseValue,
        double OrderQuantity,
        double? SizeCoefficient,
        double? ColorCoefficient,
        double LossRate,
        double QuantityNet,
        double QuantityGross,
        string Unit,
        string Formula);

    public sealed record ComponentTotalDraft(
        string ComponentCode,
        string CalculationMode,
        double TotalNet,
        double TotalGross,
        string Unit);

    public static MaterialRequirementDraft ComputeRequirement(
        FlattenedBomLine line,
        double orderQuantity,
        string variantLabel,
        double sizeCoefficient = 1.0,
        double colorCoefficient = 1.0,
        string? formulaExpression = null,
        bool applyOrderQuantity = true,
        IReadOnlyDictionary<string, double>? argumentCoefficients = null)
    {
        var sourcePath = $"BOM:{line.LineNo}";
        var coefficients = BuildArgumentCoefficients(argumentCoefficients, sizeCoefficient, colorCoefficient);

        if (line.Behavior == "CALCULATED")
        {
            const string calculationMode = "VARIABLE_MATERIAL";
            var unitNet = GenerationEngine.CalculateNetQuantityFromFormula(
                line.QuantityBase,
                formulaExpression,
                coefficients);
            var totalNet = applyOrderQuantity ? unitNet * orderQuantity : unitNet;
            var totalGross = GenerationEngine.CalculateGrossQuantity(totalNet, line.LossRate);
            var trace = new CalculationTraceDraft(
                "CBN_BOM_LINE",
                line.ComponentCode,
                calculationMode,
                $"CBN_LINE_{line.LineNo}",
                line.QuantityBase,
                orderQuantity,
                coefficients.TryGetValue("SIZE", out var sizeValue) ? sizeValue : sizeCoefficient,
                coefficients.TryGetValue("COLOR", out var colorValue) ? colorValue : colorCoefficient,
                line.LossRate,
                totalNet,
                totalGross,
                line.Unit,
                GenerationEngine.BuildFormulaTrace(
                    formulaExpression,
                    line.QuantityBase,
                    coefficients,
                    applyOrderQuantity,
                    orderQuantity,
                    line.LossRate));

            return new MaterialRequirementDraft(
                line.ComponentCode,
                variantLabel,
                calculationMode,
                totalNet,
                totalGross,
                line.Unit,
                sourcePath,
                trace);
        }

        var fixedMode = line.Behavior == "COPY_TO_VALIDATE" ? "TO_CONFIRM" : "FIXED_SUPPLY";
        var fixedNet = line.QuantityBase * orderQuantity;
        var fixedGross = line.LossRate > 0
            ? GenerationEngine.CalculateGrossQuantity(fixedNet, line.LossRate)
            : fixedNet;
        var fixedTrace = new CalculationTraceDraft(
            "CBN_BOM_LINE",
            line.ComponentCode,
            fixedMode,
            $"CBN_LINE_{line.LineNo}",
            line.QuantityBase,
            orderQuantity,
            null,
            null,
            line.LossRate,
            fixedNet,
            fixedGross,
            line.Unit,
            line.Behavior == "COPY_TO_VALIDATE"
                ? (line.LossRate > 0 ? "A confirmer: base * order_qty / (1 - loss)" : "A confirmer: base * order_qty")
                : (line.LossRate > 0 ? "base * order_qty / (1 - loss)" : "base * order_qty"));

        return new MaterialRequirementDraft(
            line.ComponentCode,
            variantLabel,
            fixedMode,
            fixedNet,
            fixedGross,
            line.Unit,
            sourcePath,
            fixedTrace);
    }

    public static IReadOnlyList<ComponentTotalDraft> AggregateTotals(
        IReadOnlyList<MaterialRequirementDraft> requirements)
    {
        return requirements
            .GroupBy(r => new { r.ComponentCode, r.Unit })
            .Select(g => new ComponentTotalDraft(
                g.Key.ComponentCode,
                g.First().CalculationMode,
                g.Sum(x => x.QuantityNet),
                g.Sum(x => x.QuantityGross),
                g.Key.Unit))
            .OrderBy(t => t.ComponentCode)
            .ToList();
    }

    private static Dictionary<string, double> BuildArgumentCoefficients(
        IReadOnlyDictionary<string, double>? argumentCoefficients,
        double sizeCoefficient,
        double colorCoefficient)
    {
        var coefficients = argumentCoefficients is null
            ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double>(argumentCoefficients, StringComparer.OrdinalIgnoreCase);

        coefficients["SIZE"] = sizeCoefficient;
        coefficients["COLOR"] = colorCoefficient;
        return coefficients;
    }
}

namespace Axioplan.GammesNomenclatures.Domain;

/// <summary>
/// Explosion multi-niveaux BOM : PF -> SF -> composants (cadrage nomenclature aplatie).
/// </summary>
public static class BomFlattener
{
    public sealed record BomLineInput(
        int LineNo,
        int ComponentArticleId,
        string ComponentCode,
        string ComponentLabel,
        string ComponentType,
        double QuantityBase,
        string Unit,
        double LossRate,
        string Behavior);

    public sealed record FlattenedBomLine(
        int LineNo,
        int ComponentArticleId,
        string ComponentCode,
        string ComponentLabel,
        string ComponentType,
        double QuantityPerUnit,
        string Unit,
        double LossRate,
        string Behavior,
        int BomLevel,
        string SourcePath);

    public static IReadOnlyList<FlattenedBomLine> Flatten(
        IReadOnlyList<BomLineInput> rootLines,
        Func<int, IReadOnlyList<BomLineInput>?> getSubBomLines,
        int maxCascadeLevel = 5)
    {
        var result = new List<FlattenedBomLine>();
        var lineCounter = 0;

        void Walk(
            IReadOnlyList<BomLineInput> lines,
            int level,
            string parentPath,
            double parentMultiplier)
        {
            if (level > maxCascadeLevel)
            {
                throw new InvalidOperationException(
                    $"Profondeur BOM max ({maxCascadeLevel}) depassee. A confirmer : PEGGING_MAX_CASCADE_LEVEL.");
            }

            foreach (var line in lines)
            {
                var path = string.IsNullOrEmpty(parentPath)
                    ? $"BOM:{line.LineNo}"
                    : $"{parentPath}>BOM:{line.LineNo}";

                var effectiveQty = line.QuantityBase * parentMultiplier;

                if (line.ComponentType == "SEMI_FINISHED")
                {
                    var subLines = getSubBomLines(line.ComponentArticleId);
                    if (subLines is { Count: > 0 })
                    {
                        lineCounter++;
                        result.Add(new FlattenedBomLine(
                            lineCounter,
                            line.ComponentArticleId,
                            line.ComponentCode,
                            line.ComponentLabel,
                            line.ComponentType,
                            effectiveQty,
                            line.Unit,
                            line.LossRate,
                            line.Behavior,
                            level,
                            path));

                        Walk(subLines, level + 1, path, effectiveQty);
                        continue;
                    }
                }

                lineCounter++;
                result.Add(new FlattenedBomLine(
                    lineCounter,
                    line.ComponentArticleId,
                    line.ComponentCode,
                    line.ComponentLabel,
                    line.ComponentType,
                    effectiveQty,
                    line.Unit,
                    line.LossRate,
                    line.Behavior,
                    level,
                    path));
            }
        }

        Walk(rootLines, 1, string.Empty, 1.0);
        return result;
    }
}

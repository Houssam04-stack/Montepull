namespace Axioplan.GammesNomenclatures.Domain.Simulation;

/// <summary>
/// Duplication taille/couleur :
/// - genere les articles variantes
/// - copie BOM avec coefficients taille et substitutions couleur
/// - copie gamme avec coefficients temps
/// </summary>
public static class SimulationDuplicationEngine
{
    public sealed record TemplateBomLine(
        int ParentArticleId,
        int ComponentArticleId,
        string ComponentCode,
        double QuantityPer,
        double ScrapRate,
        int OffsetDays,
        bool ApplySizeCoefficient,
        bool ApplyColorSubstitution);

    public sealed record TemplateRoutingOp(
        int ArticleId,
        int OperationNumber,
        string OperationName,
        string? WorkCenter,
        double SetupTimeMinutes,
        double RunTimeMinutes,
        double QueueTimeMinutes,
        double MoveTimeMinutes,
        bool ApplySizeCoefficient,
        bool ApplyColorCoefficient);

    public sealed record SubstitutionRule(
        int BaseComponentId,
        string AttributeName,
        string AttributeValue,
        int SubstituteComponentId);

    public sealed record GeneratedVariantArtifact(
        string Code,
        string Designation,
        string Size,
        string Color,
        double SizeCoefficient,
        double ColorCoefficient,
        IReadOnlyList<GeneratedBomLine> BomLines,
        IReadOnlyList<GeneratedRoutingOp> RoutingOps);

    public sealed record GeneratedBomLine(
        string ParentCode,
        string ComponentCode,
        int SourceComponentId,
        int ResolvedComponentId,
        double QuantityPer,
        double ScrapRate,
        int OffsetDays,
        bool ApplySizeCoefficient,
        bool ApplyColorSubstitution);

    public sealed record GeneratedRoutingOp(
        string ArticleCode,
        int OperationNumber,
        string OperationName,
        string? WorkCenter,
        double SetupTimeMinutes,
        double RunTimeMinutes,
        double QueueTimeMinutes,
        double MoveTimeMinutes,
        bool ApplySizeCoefficient,
        bool ApplyColorCoefficient);

    public static string BuildVariantCode(string templateCode, string color, string size)
    {
        var cleanColor = color.Trim().ToUpperInvariant().Replace(' ', '_');
        var cleanSize = size.Trim().ToUpperInvariant();
        var baseCode = templateCode.EndsWith("_BASE", StringComparison.OrdinalIgnoreCase)
            ? templateCode[..^5]
            : templateCode;
        return $"{baseCode}_{cleanColor}_{cleanSize}";
    }

    public static GeneratedVariantArtifact GenerateVariant(
        string templateCode,
        string templateDesignation,
        string size,
        string color,
        double sizeCoefficient,
        double colorCoefficient,
        IReadOnlyList<TemplateBomLine> templateBom,
        IReadOnlyList<TemplateRoutingOp> templateRouting,
        IReadOnlyList<SubstitutionRule> substitutions,
        IReadOnlyDictionary<int, string> componentCodesById)
    {
        var code = BuildVariantCode(templateCode, color, size);
        var bom = new List<GeneratedBomLine>();
        foreach (var line in templateBom)
        {
            var resolvedId = line.ComponentArticleId;
            if (line.ApplyColorSubstitution)
            {
                var rule = substitutions.FirstOrDefault(r =>
                    r.BaseComponentId == line.ComponentArticleId
                    && string.Equals(r.AttributeName, "COLOR", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.AttributeValue, color, StringComparison.OrdinalIgnoreCase));
                if (rule is not null)
                {
                    resolvedId = rule.SubstituteComponentId;
                }
            }

            var qty = line.ApplySizeCoefficient
                ? line.QuantityPer * sizeCoefficient
                : line.QuantityPer;

            if (!componentCodesById.TryGetValue(resolvedId, out var componentCode))
            {
                componentCode = componentCodesById.GetValueOrDefault(line.ComponentArticleId, line.ComponentCode);
            }

            bom.Add(new GeneratedBomLine(
                code,
                componentCode,
                line.ComponentArticleId,
                resolvedId,
                qty,
                line.ScrapRate,
                line.OffsetDays,
                line.ApplySizeCoefficient,
                line.ApplyColorSubstitution));
        }

        var routing = templateRouting.Select(op =>
        {
            var sizeFactor = op.ApplySizeCoefficient ? sizeCoefficient : 1.0;
            var colorFactor = op.ApplyColorCoefficient ? colorCoefficient : 1.0;
            var factor = sizeFactor * colorFactor;
            return new GeneratedRoutingOp(
                code,
                op.OperationNumber,
                op.OperationName,
                op.WorkCenter,
                op.SetupTimeMinutes * factor,
                op.RunTimeMinutes * factor,
                op.QueueTimeMinutes,
                op.MoveTimeMinutes,
                op.ApplySizeCoefficient,
                op.ApplyColorCoefficient);
        }).ToList();

        return new GeneratedVariantArtifact(
            code,
            $"{templateDesignation} {color} {size}",
            size,
            color,
            sizeCoefficient,
            colorCoefficient,
            bom,
            routing);
    }
}

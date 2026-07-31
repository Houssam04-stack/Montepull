namespace Axioplan.GammesNomenclatures.Domain.Mvp0;

/// <summary>
/// Mapping MVP-0 working set → referentiel APS / legacy (Gate 0→1, dossier §7.2).
/// </summary>
public static class Mvp0ApsBridgeMapper
{
    public static string MapArticleType(string? mvp0Type) => mvp0Type?.Trim().ToUpperInvariant() switch
    {
        "FINISHED" or "FINISHED_GOOD" or "PRODUIT_FINI" or "PF" => "FINISHED_GOOD",
        "SEMI" or "SEMI_FINISHED" or "COMPOSE" or "COMPOSED" or "SF" => "SEMI_FINISHED",
        "COMPONENT" or "COMPOSANT" or "RM" => "COMPONENT",
        "SERVICE" => "SERVICE",
        _ => "COMPONENT"
    };

    public static string ResolveUnit(string? unit) =>
        string.IsNullOrWhiteSpace(unit) ? "PIECE" : unit.Trim().ToUpperInvariant();

    /// <summary>
    /// Heuristique : la consommation BOM se rattache à la dernière opération de la gamme parent.
    /// </summary>
    public static string? ResolveOperationCode(
        string parentArticle,
        IReadOnlyList<Mvp0RoutingOpRow> ops)
    {
        var parentOps = ops.Where(o => string.Equals(o.Article, parentArticle, StringComparison.OrdinalIgnoreCase)).ToList();
        if (parentOps.Count == 0) return null;
        return parentOps.OrderByDescending(o => o.Sequence).First().OpCode;
    }
}

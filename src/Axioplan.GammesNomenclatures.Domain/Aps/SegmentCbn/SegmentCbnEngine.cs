namespace Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;

public static class ApsBathCompatRules
{
    public const string Piece = "PIECE";
    public const string Garment = "VETEMENT";
    public const string CustomerLot = "LOT_CLIENT";
}

public sealed record ApsYarnBathStock(
    string BathCode,
    string ArticleCode,
    double QuantityFree,
    string Unit,
    string Status);

public sealed record ApsYarnAtpResult(
    double Available,
    string? SelectedBathCode,
    IReadOnlyList<string> CompatibleBaths,
    string Rule,
    string Justification,
    double Shortage);

/// <summary>
/// ATP fil non additif : MAX sur ensembles de bains compatibles, jamais SOMME (CDC §7.3).
/// </summary>
public static class YarnBathAvailabilityEngine
{
    public static ApsYarnAtpResult Evaluate(
        IReadOnlyList<ApsYarnBathStock> baths,
        double requiredQty,
        string customerCompatRule)
    {
        // MVP : pour PIECE / VETEMENT / LOT_CLIENT, un besoin est couvert par UN seul bain
        // (pas de mix de bains sur une meme piece). TO_CONFIRM : ensembles multi-bains si regle evolue.
        // QuantityFree doit deja refleter ConsumableQuantity (LIBRE / reserve commande / MTS arbitre).
        var free = baths.Where(b => b.QuantityFree > 0).ToList();

        if (free.Count == 0)
        {
            return new ApsYarnAtpResult(0, null, [], customerCompatRule,
                "Aucun bain consommable — ATP = 0", requiredQty);
        }

        var best = free.OrderByDescending(b => b.QuantityFree).First();
        // MAX, pas SOMME
        var available = best.QuantityFree;
        var sumWouldBeWrong = free.Sum(b => b.QuantityFree);

        var justification =
            $"Regle {customerCompatRule} : un bain unique (pas de melange). " +
            $"ATP = MAX(bains) = {available:F2} (bain {best.BathCode}). " +
            $"SOMME interdite = {sumWouldBeWrong:F2} (non utilisee).";

        var shortage = Math.Max(0, requiredQty - available);
        return new ApsYarnAtpResult(
            available,
            best.BathCode,
            free.Select(b => b.BathCode).ToList(),
            customerCompatRule,
            justification,
            shortage);
    }
}

public sealed record ApsCompiledNeed(
    string ComponentCode,
    double QuantityPer,
    double OffsetDays,
    string BathConstraint,
    string SourcePath);

public sealed record ApsStockPosition(
    string ArticleCode,
    string Status,
    double Quantity,
    string? BathCode,
    string? ReservedForAggregateId);

public sealed record ApsSegmentDemand(
    string DemandId,
    string FinishedArticleCode,
    double Quantity,
    DateOnly NeedDate,
    string CustomerCompatRule,
    bool IsMtsReplenishment,
    string? OrderAggregateId);

public sealed record ApsSegmentNeedLine(
    string Segment,
    string ArticleCode,
    double GrossNeed,
    double SafetyStock,
    double Available,
    double NetNeed,
    double QtyToLaunch,
    string? SelectedBath,
    string SourcePath,
    string ArtifactHash,
    bool Blocked,
    string BlockReason,
    IReadOnlyList<string> Traces);

public sealed record ApsSegmentCbnResult(
    string DemandId,
    string Segment,
    string FinishedArticleCode,
    IReadOnlyList<ApsSegmentNeedLine> Lines,
    bool Success,
    string? Error);

/// <summary>
/// CBN APS par segment — separe du CBN legacy. Artefact VALID obligatoire.
/// </summary>
public static class SegmentCbnEngine
{
    public static ApsSegmentCbnResult Run(
        ApsSegmentDemand demand,
        string segment,
        bool artifactValid,
        string artifactHash,
        IReadOnlyList<ApsCompiledNeed> compiledNeeds,
        IReadOnlyList<ApsStockPosition> stocks,
        double lotMultiple = 1,
        double safetyStock = 0)
    {
        if (!artifactValid)
        {
            return new ApsSegmentCbnResult(
                demand.DemandId,
                segment,
                demand.FinishedArticleCode,
                [],
                false,
                "CompileInvalideRefuse : artefact INVALID — CBN APS echoue (I-comp).");
        }

        if (compiledNeeds.Count == 0)
        {
            return new ApsSegmentCbnResult(
                demand.DemandId,
                segment,
                demand.FinishedArticleCode,
                [],
                false,
                "Aucun besoin compile pour ce segment.");
        }

        var lines = new List<ApsSegmentNeedLine>();
        foreach (var need in compiledNeeds)
        {
            var traces = new List<string>();
            var gross = demand.Quantity * need.QuantityPer;
            traces.Add($"Besoin_brut = qte_aval {demand.Quantity:F4} × artefact {need.QuantityPer:F4} = {gross:F4} (offset {need.OffsetDays} j, non reapplique rendement)");

            // Netting s'arrete au stock du segment — pas de traversal decouplage.
            double available;
            string? selectedBath = null;
            if (segment == "SEG_A")
            {
                var baths = stocks
                    .Where(s => s.ArticleCode.Equals(need.ComponentCode, StringComparison.OrdinalIgnoreCase))
                    .Select(s => new ApsYarnBathStock(
                        s.BathCode ?? "NO_BATH",
                        s.ArticleCode,
                        ConsumableQuantity(s, demand),
                        "KG",
                        s.Status))
                    .ToList();

                var atp = YarnBathAvailabilityEngine.Evaluate(baths, gross + safetyStock, demand.CustomerCompatRule);
                available = atp.Available;
                selectedBath = atp.SelectedBathCode;
                traces.Add(atp.Justification);
            }
            else
            {
                available = stocks
                    .Where(s => s.ArticleCode.Equals(need.ComponentCode, StringComparison.OrdinalIgnoreCase))
                    .Sum(s => ConsumableQuantity(s, demand));
                traces.Add($"Stock consommable (LIBRE + reserve commande propre) = {available:F4}");
            }

            if (demand.IsMtsReplenishment)
            {
                traces.Add("Demande MTS = ordre de plein droit (pas un residuel).");
            }

            var reservedMtsBlocked = stocks.Any(s =>
                s.ArticleCode.Equals(need.ComponentCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Status, "RESERVE_MTS", StringComparison.OrdinalIgnoreCase)
                && !demand.IsMtsReplenishment
                && s.Quantity > 0);
            if (reservedMtsBlocked)
            {
                traces.Add("RESERVE_MTS present mais non consomme (arbitrage explicite requis).");
            }

            var net = Math.Max(0, gross + safetyStock - available);
            traces.Add($"Besoin_net = max(0, brut {gross:F4} + secu {safetyStock:F4} − dispo {available:F4}) = {net:F4}");

            var multiple = lotMultiple > 0 ? lotMultiple : 1;
            var qtyLaunch = net <= 0 ? 0 : Math.Ceiling(net / multiple) * multiple;
            traces.Add($"Qte_a_lancer = arrondi_lot({net:F4}, multiple {multiple}) = {qtyLaunch:F4}");

            var blocked = segment == "SEG_A" && net > 0 && selectedBath is null;
            lines.Add(new ApsSegmentNeedLine(
                segment,
                need.ComponentCode,
                gross,
                safetyStock,
                available,
                net,
                qtyLaunch,
                selectedBath,
                need.SourcePath,
                artifactHash,
                blocked,
                blocked ? "Aucun bain compatible" : string.Empty,
                traces));
        }

        return new ApsSegmentCbnResult(
            demand.DemandId,
            segment,
            demand.FinishedArticleCode,
            lines,
            true,
            null);
    }

    private static double ConsumableQuantity(ApsStockPosition stock, ApsSegmentDemand demand)
    {
        if (string.Equals(stock.Status, "LIBRE", StringComparison.OrdinalIgnoreCase))
        {
            return stock.Quantity;
        }

        if (string.Equals(stock.Status, "RESERVE_COMMANDE", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(demand.OrderAggregateId)
            && string.Equals(stock.ReservedForAggregateId, demand.OrderAggregateId, StringComparison.OrdinalIgnoreCase))
        {
            return stock.Quantity;
        }

        if (string.Equals(stock.Status, "RESERVE_MTS", StringComparison.OrdinalIgnoreCase)
            && demand.IsMtsReplenishment)
        {
            return stock.Quantity;
        }

        // RESERVE_MTS pour MTO : 0 sans arbitrage
        return 0;
    }
}

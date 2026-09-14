namespace Axioplan.GammesNomenclatures.Domain;

/// <summary>
/// Premier pegging conforme au flux documente :
/// Demande -> Nomenclature aplatie -> Besoins -> Stock / achats / OF -> Pegging
/// Regles d'allocation detaillees : A confirmer avec le metier.
/// </summary>
public static class PeggingEngine
{
    public sealed record MaterialNeed(
        int RequirementId,
        int? SalesOrderLineId,
        int ComponentArticleId,
        string ComponentCode,
        string ComponentType,
        double QuantityGross,
        string Unit,
        string VariantLabel);

    public sealed record SupplyCandidate(
        string EntityType,
        int EntityId,
        string EntityCode,
        int ArticleId,
        double QuantityAvailable,
        string Unit);

    public sealed record PeggingLinkDraft(
        string LinkType,
        string Direction,
        string SourceEntityType,
        int SourceEntityId,
        string TargetEntityType,
        int TargetEntityId,
        int? MaterialRequirementId,
        double Quantity,
        string Unit,
        string SourcePath);

    public static IReadOnlyList<PeggingLinkDraft> BuildLinks(
        IReadOnlyList<MaterialNeed> needs,
        IReadOnlyList<SupplyCandidate> salesLines,
        IReadOnlyList<SupplyCandidate> manufacturingOrders,
        IReadOnlyList<SupplyCandidate> purchaseOrders,
        IReadOnlyList<SupplyCandidate> stockBalances,
        bool bidirectionalLinks = true)
    {
        var links = new List<PeggingLinkDraft>();

        foreach (var need in needs)
        {
            var salesLine = salesLines.FirstOrDefault(s =>
                need.SalesOrderLineId.HasValue && s.EntityId == need.SalesOrderLineId.Value);

            if (salesLine is not null)
            {
                var ovQty = Math.Min(need.QuantityGross, salesLine.QuantityAvailable);
                if (ovQty > 0)
                {
                    links.Add(new PeggingLinkDraft(
                        "OV_TO_NEED", "DOWNSTREAM",
                        "SALES_ORDER_LINE", salesLine.EntityId,
                        "MATERIAL_REQUIREMENT", need.RequirementId,
                        need.RequirementId, ovQty, need.Unit,
                        $"OV:{salesLine.EntityCode}->NEED:{need.ComponentCode}"));

                    if (bidirectionalLinks)
                    {
                        links.Add(new PeggingLinkDraft(
                            "OV_TO_NEED", "UPSTREAM",
                            "MATERIAL_REQUIREMENT", need.RequirementId,
                            "SALES_ORDER_LINE", salesLine.EntityId,
                            need.RequirementId, ovQty, need.Unit,
                            $"NEED:{need.ComponentCode}->OV:{salesLine.EntityCode}"));
                    }
                }
            }

            var remaining = need.QuantityGross;

            remaining = AllocateFromSupplies(
                links, need, remaining,
                stockBalances.Where(s => s.ArticleId == need.ComponentArticleId).ToList(),
                "NEED_TO_STOCK", "STOCK_TO_NEED", "STOCK_BALANCE", bidirectionalLinks);

            if (remaining > 0 && (need.ComponentType is "FINISHED_GOOD" or "SEMI_FINISHED"))
            {
                remaining = AllocateFromSupplies(
                    links, need, remaining, manufacturingOrders,
                    "NEED_TO_OF", "OF_TO_NEED", "MANUFACTURING_ORDER", bidirectionalLinks);
            }

            if (remaining > 0 && (need.ComponentType == "COMPONENT" || need.ComponentType == "SEMI_FINISHED"))
            {
                remaining = AllocateFromSupplies(
                    links, need, remaining, purchaseOrders,
                    "NEED_TO_OA", "OA_TO_NEED", "PURCHASE_ORDER_LINE", bidirectionalLinks);
            }
        }

        return links;
    }

    private static double AllocateFromSupplies(
        List<PeggingLinkDraft> links,
        MaterialNeed need,
        double remaining,
        IReadOnlyList<SupplyCandidate> supplies,
        string downstreamType,
        string upstreamType,
        string supplyEntityType,
        bool bidirectional)
    {
        foreach (var supply in supplies.Where(s => s.ArticleId == need.ComponentArticleId))
        {
            if (remaining <= 0)
            {
                break;
            }

            var alreadyAllocated = links
                .Where(link => link.Direction == "DOWNSTREAM"
                    && link.SourceEntityType == "MATERIAL_REQUIREMENT"
                    && link.TargetEntityType == supplyEntityType
                    && link.TargetEntityId == supply.EntityId)
                .Sum(link => link.Quantity);
            var available = Math.Max(0, supply.QuantityAvailable - alreadyAllocated);
            var qty = Math.Min(remaining, available);
            if (qty <= 0)
            {
                continue;
            }

            links.Add(new PeggingLinkDraft(
                downstreamType, "DOWNSTREAM",
                "MATERIAL_REQUIREMENT", need.RequirementId,
                supplyEntityType, supply.EntityId,
                need.RequirementId, qty, need.Unit,
                $"NEED:{need.ComponentCode}->{supplyEntityType}:{supply.EntityCode}"));

            if (bidirectional)
            {
                links.Add(new PeggingLinkDraft(
                    upstreamType, "UPSTREAM",
                    supplyEntityType, supply.EntityId,
                    "MATERIAL_REQUIREMENT", need.RequirementId,
                    need.RequirementId, qty, need.Unit,
                    $"{supplyEntityType}:{supply.EntityCode}->NEED:{need.ComponentCode}"));
            }

            remaining -= qty;
        }

        return remaining;
    }
}

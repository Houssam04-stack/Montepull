namespace Axioplan.GammesNomenclatures.Domain.Simulation;

/// <summary>
/// Moteur CBN/MRP de simulation.
/// Ordre classique :
/// 1) validation / cycles BOM
/// 2) LLC (Low-Level Code)
/// 3) explosion controlee (vue aplatie sans perdre niveaux/parents/dates/chemins)
/// 4) besoin brut
/// 5) besoin net
/// 6) lot sizing
/// 7) POR / PORec
/// 8) OF / OA
/// 9) pegging dynamique
/// </summary>
public static class SimulationCbnEngine
{
    public static SimulationCbnRunResult Run(
        SimSalesOrderInput salesOrder,
        IReadOnlyList<SimArticleInput> articles,
        IReadOnlyList<SimBomLineInput> bomLines,
        IReadOnlyDictionary<int, SimCbnParameterInput> parameters,
        DateOnly today)
    {
        var alerts = new List<CbnAlertResult>();
        var byId = articles.ToDictionary(a => a.Id);
        if (!byId.ContainsKey(salesOrder.ArticleId))
        {
            alerts.Add(new CbnAlertResult(salesOrder.ArticleId, SimulationAlertTypes.InvalidBom,
                "Article demande introuvable dans la simulation.", "ERROR"));
            return Empty(alerts);
        }

        // --- Detection de cycles ---
        if (DetectCycle(salesOrder.ArticleId, bomLines, out var cyclePath))
        {
            alerts.Add(new CbnAlertResult(salesOrder.ArticleId, SimulationAlertTypes.BomCycle,
                $"Cycle detecte dans la nomenclature : {cyclePath}", "ERROR"));
            return Empty(alerts);
        }

        // --- LLC : LLC(enfant) = max(LLC(enfant), LLC(parent)+1) ---
        var llc = ComputeLlc(salesOrder.ArticleId, bomLines);
        foreach (var article in articles)
        {
            if (!parameters.ContainsKey(article.Id) && IsReachable(article.Id, salesOrder.ArticleId, bomLines))
            {
                alerts.Add(new CbnAlertResult(article.Id, SimulationAlertTypes.MissingCbnParameter,
                    $"Article {article.Code} sans parametres CBN simules.", "WARNING"));
            }
        }

        // --- Explosion controlee (vue de calcul) ---
        var flat = BuildControlledFlatBom(
            salesOrder.ArticleId,
            salesOrder.RequestedDate,
            bomLines,
            byId,
            alerts);

        // --- Besoin brut (racine = qty commande ; composants via POR parent * qty/par) ---
        // Premiere version : accumulation par article (sum des cumuls) + conservation parent/niveau/chemin sur les lignes flat.
        var grossLines = CalculateGrossRequirements(salesOrder, flat, byId);
        var grossByArticle = grossLines
            .GroupBy(g => g.ArticleId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.GrossQuantity));

        // --- Besoin net + lot sizing + POR/PORec, traites par LLC croissant ---
        var netResults = new List<NetRequirementResult>();
        var planned = new List<PlannedOrderResult>();
        var workOrders = new List<WorkOrderResult>();
        var plannedReleaseQty = new Dictionary<int, double>(); // pour explosion dependante

        // Recompute gross for components using PlannedOrderRelease of parents when available.
        // For MVP without multi-period MRP buckets: POR of FG * QtyPer = component gross.
        // We still expose flat lines with parent/path, and aggregate nets by article.
        var dependencyGross = BuildDependentGrossFromFlat(salesOrder, flat);

        foreach (var articleId in llc.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key))
        {
            if (!byId.TryGetValue(articleId, out var article))
            {
                continue;
            }

            var gross = dependencyGross.GetValueOrDefault(articleId, 0);
            if (articleId == salesOrder.ArticleId)
            {
                gross = salesOrder.Quantity;
            }

            parameters.TryGetValue(articleId, out var param);
            param ??= new SimCbnParameterInput(articleId, 0, 0, 0, 0, 0, article.LeadTimeDays, SimulationLotRules.LotForLot, 0, 1);

            if (param.LeadTimeDays <= 0 && article.ProcurementType == SimulationProcurementTypes.Manufactured)
            {
                alerts.Add(new CbnAlertResult(articleId, SimulationAlertTypes.MissingLeadTime,
                    $"Article {article.Code} sans delai (lead time).", "WARNING"));
            }

            // Need date: earliest related flat need, or order date for FG
            var needDate = articleId == salesOrder.ArticleId
                ? salesOrder.RequestedDate
                : flat.Where(f => f.ComponentArticleId == articleId).Select(f => f.NeedDate).DefaultIfEmpty(salesOrder.RequestedDate).Min();

            var net = CalculateNetRequirement(gross, param);
            netResults.Add(new NetRequirementResult(
                articleId,
                article.Code,
                gross,
                param.OnHandStock,
                param.SafetyStock,
                param.ReservedQuantity,
                param.ScheduledReceiptProduction,
                param.ScheduledReceiptPurchase,
                net,
                needDate));

            if (net <= 0)
            {
                plannedReleaseQty[articleId] = 0;
                continue;
            }

            var lotQty = ApplyLotSizing(net, param);
            var leadTime = param.LeadTimeDays > 0 ? param.LeadTimeDays : article.LeadTimeDays;
            // PORec = date de couverture du besoin (ReceiptDate)
            // POR  = PORec - LeadTime (ReleaseDate)
            var receiptDate = needDate;
            var releaseDate = receiptDate.AddDays(-leadTime);
            if (releaseDate < today)
            {
                alerts.Add(new CbnAlertResult(articleId, SimulationAlertTypes.PastRelease,
                    $"Lancement dans le passe pour {article.Code} (POR={releaseDate:yyyy-MM-dd}).", "WARNING"));
            }

            if (needDate < today)
            {
                alerts.Add(new CbnAlertResult(articleId, SimulationAlertTypes.PastNeed,
                    $"Besoin passe pour {article.Code} (date={needDate:yyyy-MM-dd}).", "WARNING"));
            }

            var orderType = article.ProcurementType == SimulationProcurementTypes.Manufactured
                ? SimulationOrderTypes.Of
                : SimulationOrderTypes.Oa;

            planned.Add(new PlannedOrderResult(
                articleId,
                article.Code,
                orderType,
                lotQty,
                needDate,
                releaseDate,
                receiptDate,
                articleId == salesOrder.ArticleId ? "SALES_ORDER" : "DEPENDENT_DEMAND",
                BuildPlannedOrderJustification(
                    article,
                    orderType,
                    gross,
                    net,
                    lotQty,
                    param,
                    articleId == salesOrder.ArticleId,
                    salesOrder.CustomerOrderNumber)));

            plannedReleaseQty[articleId] = lotQty;

            if (orderType == SimulationOrderTypes.Of)
            {
                workOrders.Add(new WorkOrderResult(
                    articleId,
                    article.Code,
                    lotQty,
                    releaseDate,
                    receiptDate,
                    "PLANNED"));
            }
        }

        // --- Pegging dynamique ---
        var pegging = BuildPeggingTree(salesOrder, byId, flat, planned);

        return new SimulationCbnRunResult(
            llc,
            flat,
            grossLines,
            netResults.OrderBy(n => llc.GetValueOrDefault(n.ArticleId)).ThenBy(n => n.ArticleCode).ToList(),
            planned,
            workOrders,
            pegging,
            alerts);
    }

    /// <summary>
    /// Low-Level Code : plus profond gagne quand un article apparait a plusieurs niveaux.
    /// </summary>
    public static Dictionary<int, int> ComputeLlc(int rootArticleId, IReadOnlyList<SimBomLineInput> bomLines)
    {
        var children = bomLines
            .GroupBy(b => b.ParentArticleId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var llc = new Dictionary<int, int> { [rootArticleId] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(rootArticleId);

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            var parentLlc = llc[parent];
            if (!children.TryGetValue(parent, out var kids))
            {
                continue;
            }

            foreach (var kid in kids)
            {
                var candidate = parentLlc + 1;
                if (!llc.TryGetValue(kid.ComponentArticleId, out var existing) || candidate > existing)
                {
                    llc[kid.ComponentArticleId] = candidate;
                    queue.Enqueue(kid.ComponentArticleId);
                }
            }
        }

        return llc;
    }

    public static bool DetectCycle(
        int rootArticleId,
        IReadOnlyList<SimBomLineInput> bomLines,
        out string path)
    {
        var children = bomLines
            .GroupBy(b => b.ParentArticleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ComponentArticleId).ToList());
        var visiting = new HashSet<int>();
        var visited = new HashSet<int>();
        var stack = new List<int>();
        string? foundPath = null;

        bool Dfs(int node)
        {
            if (visiting.Contains(node))
            {
                foundPath = string.Join(" > ", stack.Append(node));
                return true;
            }

            if (visited.Contains(node))
            {
                return false;
            }

            visiting.Add(node);
            stack.Add(node);
            if (children.TryGetValue(node, out var kids))
            {
                foreach (var kid in kids)
                {
                    if (Dfs(kid))
                    {
                        return true;
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(node);
            visited.Add(node);
            return false;
        }

        var hasCycle = Dfs(rootArticleId);
        path = foundPath ?? string.Empty;
        return hasCycle;
    }

    /// <summary>
    /// Vue aplatie CONTROLEE : conserve niveau, parent, composant, qty/par, qty cumulee, date besoin, path.
    /// Ne remplace jamais la nomenclature multi-niveaux d'origine.
    /// </summary>
    public static IReadOnlyList<ControlledFlatBomLine> BuildControlledFlatBom(
        int rootArticleId,
        DateOnly rootNeedDate,
        IReadOnlyList<SimBomLineInput> bomLines,
        IReadOnlyDictionary<int, SimArticleInput> articles,
        List<CbnAlertResult> alerts,
        int maxDepth = 20)
    {
        var children = bomLines
            .GroupBy(b => b.ParentArticleId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<ControlledFlatBomLine>();
        var rootCode = articles.TryGetValue(rootArticleId, out var root) ? root.Code : rootArticleId.ToString();

        void Walk(int parentId, string? parentCode, DateOnly parentNeedDate, double parentCumulative, string parentPath, int level)
        {
            if (level > maxDepth)
            {
                alerts.Add(new CbnAlertResult(parentId, SimulationAlertTypes.InvalidBom,
                    $"Profondeur BOM max ({maxDepth}) depassee.", "ERROR"));
                return;
            }

            if (!children.TryGetValue(parentId, out var kids))
            {
                return;
            }

            foreach (var line in kids)
            {
                if (!articles.TryGetValue(line.ComponentArticleId, out var component))
                {
                    alerts.Add(new CbnAlertResult(line.ComponentArticleId, SimulationAlertTypes.MissingComponent,
                        $"Composant id={line.ComponentArticleId} manquant.", "ERROR"));
                    continue;
                }

                // Quantity with scrap: qty_per / (1 - scrap)
                var effectivePer = line.ScrapRate is > 0 and < 1
                    ? line.QuantityPer / (1 - line.ScrapRate)
                    : line.QuantityPer;
                var cumulative = parentCumulative * effectivePer;
                var needDate = parentNeedDate.AddDays(-line.OffsetDays);
                var path = string.IsNullOrEmpty(parentPath)
                    ? $"{rootCode} > {component.Code}"
                    : $"{parentPath} > {component.Code}";

                result.Add(new ControlledFlatBomLine(
                    rootArticleId,
                    parentId,
                    parentCode,
                    component.Id,
                    component.Code,
                    level,
                    effectivePer,
                    cumulative,
                    needDate,
                    path,
                    line.OffsetDays));

                Walk(component.Id, component.Code, needDate, cumulative, path, level + 1);
            }
        }

        Walk(rootArticleId, rootCode, rootNeedDate, 1.0, rootCode, 1);
        return result;
    }

    /// <summary>
    /// Besoin net =
    /// Gross + Reserved + SafetyStock - OnHand - ScheduledReceiptProd - ScheduledReceiptPurchase
    /// plancher a 0.
    /// </summary>
    public static double CalculateNetRequirement(double gross, SimCbnParameterInput param)
    {
        var net = gross
               + param.ReservedQuantity
               + param.SafetyStock
               - param.OnHandStock
               - param.ScheduledReceiptProduction
               - param.ScheduledReceiptPurchase;
        return Math.Max(0, net);
    }

    public static double ApplyLotSizing(double netRequirement, SimCbnParameterInput param)
    {
        return param.LotRule switch
        {
            SimulationLotRules.FixedQuantity => param.MinLot > 0 ? param.MinLot : netRequirement,
            SimulationLotRules.MinimumQuantity => Math.Max(netRequirement, param.MinLot),
            SimulationLotRules.MultipleQuantity => RoundUpToMultiple(netRequirement, param.MultipleLot > 0 ? param.MultipleLot : 1),
            _ => netRequirement // LotForLot
        };
    }

    private static double RoundUpToMultiple(double value, double multiple)
    {
        if (multiple <= 0)
        {
            return value;
        }

        return Math.Ceiling(value / multiple) * multiple;
    }

    private static IReadOnlyList<GrossRequirementResult> CalculateGrossRequirements(
        SimSalesOrderInput salesOrder,
        IReadOnlyList<ControlledFlatBomLine> flat,
        IReadOnlyDictionary<int, SimArticleInput> articles)
    {
        var root = articles[salesOrder.ArticleId];
        var lines = new List<GrossRequirementResult>
        {
            new(
                salesOrder.ArticleId,
                root.Code,
                null,
                0,
                salesOrder.Quantity,
                salesOrder.RequestedDate,
                "SALES_ORDER",
                root.Code)
        };

        foreach (var line in flat)
        {
            lines.Add(new GrossRequirementResult(
                line.ComponentArticleId,
                line.ComponentArticleCode,
                line.ParentArticleId,
                line.Level,
                salesOrder.Quantity * line.CumulativeQuantity,
                line.NeedDate,
                "DEPENDENT_DEMAND",
                line.Path));
        }

        return lines;
    }

    private static Dictionary<int, double> BuildDependentGrossFromFlat(
        SimSalesOrderInput salesOrder,
        IReadOnlyList<ControlledFlatBomLine> flat)
    {
        var map = new Dictionary<int, double> { [salesOrder.ArticleId] = salesOrder.Quantity };
        foreach (var group in flat.GroupBy(f => f.ComponentArticleId))
        {
            map[group.Key] = group.Sum(x => salesOrder.Quantity * x.CumulativeQuantity);
        }

        return map;
    }

    private static IReadOnlyList<PeggingNodeResult> BuildPeggingTree(
        SimSalesOrderInput salesOrder,
        IReadOnlyDictionary<int, SimArticleInput> articles,
        IReadOnlyList<ControlledFlatBomLine> flat,
        IReadOnlyList<PlannedOrderResult> planned)
    {
        var root = articles[salesOrder.ArticleId];
        var rootPlanned = planned.FirstOrDefault(p => p.ArticleId == salesOrder.ArticleId);
        var rootPath = salesOrder.CustomerOrderNumber
                       + (rootPlanned is null ? string.Empty : $" > {rootPlanned.OrderType}_{root.Code}")
                       + $" > {root.Code}";

        var childrenByParent = flat
            .Where(f => f.Level == 1)
            .Select(f => BuildPeggingNode(f, flat, salesOrder, planned))
            .ToList();

        return
        [
            new PeggingNodeResult(
                salesOrder.ArticleId,
                root.Code,
                null,
                0,
                salesOrder.Quantity,
                salesOrder.RequestedDate,
                "SALES_ORDER",
                rootPath,
                childrenByParent)
        ];
    }

    private static PeggingNodeResult BuildPeggingNode(
        ControlledFlatBomLine line,
        IReadOnlyList<ControlledFlatBomLine> all,
        SimSalesOrderInput salesOrder,
        IReadOnlyList<PlannedOrderResult> planned)
    {
        var kids = all
            .Where(f => f.ParentArticleId == line.ComponentArticleId && f.Path.StartsWith(line.Path, StringComparison.Ordinal))
            .Where(f => f.Level == line.Level + 1)
            .Select(f => BuildPeggingNode(f, all, salesOrder, planned))
            .ToList();

        var qty = salesOrder.Quantity * line.CumulativeQuantity;
        var order = planned.FirstOrDefault(p => p.ArticleId == line.ComponentArticleId);
        var source = order?.OrderType ?? "DEPENDENT_DEMAND";

        return new PeggingNodeResult(
            line.ComponentArticleId,
            line.ComponentArticleCode,
            line.ParentArticleId,
            line.Level,
            qty,
            line.NeedDate,
            source,
            line.Path,
            kids);
    }

    private static bool IsReachable(int articleId, int rootId, IReadOnlyList<SimBomLineInput> bomLines)
    {
        if (articleId == rootId)
        {
            return true;
        }

        var llc = ComputeLlc(rootId, bomLines);
        return llc.ContainsKey(articleId);
    }

    /// <summary>
    /// Explique pourquoi un OF (fabriquer) ou un OA (acheter) est propose.
    /// </summary>
    internal static string BuildPlannedOrderJustification(
        SimArticleInput article,
        string orderType,
        double gross,
        double net,
        double lotQty,
        SimCbnParameterInput param,
        bool isFinishedGoodDemand,
        string customerOrderNumber)
    {
        var available = param.OnHandStock + param.ScheduledReceiptProduction + param.ScheduledReceiptPurchase;
        var sourceLabel = isFinishedGoodDemand
            ? $"demande client {customerOrderNumber}"
            : "besoin dependant (composant de la nomenclature)";

        if (orderType == SimulationOrderTypes.Of)
        {
            return $"OF — a fabriquer : article parametre « Manufactured » ({article.Designation}). "
                   + $"Declenche par {sourceLabel}. "
                   + $"Besoin brut {gross:F2} ; disponible (stock {param.OnHandStock:F2} + encours OF {param.ScheduledReceiptProduction:F2} + achats {param.ScheduledReceiptPurchase:F2}) = {available:F2} ; "
                   + $"stock securite {param.SafetyStock:F2} ; reserve {param.ReservedQuantity:F2} "
                   + $"→ besoin net {net:F2}. Quantite OF proposee : {lotQty:F2} (apres regle de lot).";
        }

        return $"OA — a acheter : article parametre « Purchased » ({article.Designation}), pas fabrique en interne. "
               + $"Declenche par {sourceLabel}. "
               + $"Besoin brut {gross:F2} ; disponible (stock {param.OnHandStock:F2} + encours OF {param.ScheduledReceiptProduction:F2} + achats {param.ScheduledReceiptPurchase:F2}) = {available:F2} ; "
               + $"stock securite {param.SafetyStock:F2} ; reserve {param.ReservedQuantity:F2} "
               + $"→ besoin net {net:F2} > stock + arrivages. Quantite OA proposee : {lotQty:F2} (apres regle de lot).";
    }

    private static SimulationCbnRunResult Empty(List<CbnAlertResult> alerts)
        => new SimulationCbnRunResult(
            new Dictionary<int, int>(),
            Array.Empty<ControlledFlatBomLine>(),
            Array.Empty<GrossRequirementResult>(),
            Array.Empty<NetRequirementResult>(),
            Array.Empty<PlannedOrderResult>(),
            Array.Empty<WorkOrderResult>(),
            Array.Empty<PeggingNodeResult>(),
            alerts);
}

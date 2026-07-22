namespace Axioplan.GammesNomenclatures.Domain.Mvp0;

public static class Mvp0CampaignStatuses
{
    public const string Draft = "DRAFT";
    public const string Importing = "IMPORTING";
    public const string Validating = "VALIDATING";
    public const string NeedsCorrection = "NEEDS_CORRECTION";
    public const string ReadyForBacktest = "READY_FOR_BACKTEST";
    public const string Backtested = "BACKTESTED";
    public const string Go = "GO";
    public const string NoGo = "NO_GO";
    public const string Archived = "ARCHIVED";
}

public static class Mvp0Severities
{
    public const string Blocking = "BLOCKING";
    public const string Warning = "WARNING";
    public const string Information = "INFORMATION";
}

public static class Mvp0Provenance
{
    public const string Real = "REAL";
    public const string Simulated = "SIMULATED";
    public const string ToConfirm = "TO_CONFIRM";
}

public static class Mvp0GateOutcomes
{
    public const string Go = "GO";
    public const string GoWithReservations = "GO_WITH_RESERVATIONS";
    public const string NoGo = "NO_GO";
    public const string InsufficientData = "INSUFFICIENT_DATA";
}

public static class Mvp0FreshnessBands
{
    public const string Fresh = "FRESH";
    public const string Aging = "AGING";
    public const string Stale = "STALE";
    public const string Unknown = "UNKNOWN";
}

public sealed record Mvp0Campaign(
    Guid Id,
    string Code,
    string FamilyCode,
    string SiteCode,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string Owner,
    string Status,
    string DataSource,
    string Provenance,
    DateTime CreatedAtUtc,
    int ImportVersion,
    double GoThreshold,
    double GoWithReservationsThreshold,
    string? GateOutcome);

public sealed record Mvp0Anomaly(
    string AnomalyId,
    string RuleCode,
    string Severity,
    string ObjectType,
    string ObjectKey,
    string? FoundValue,
    string? ExpectedValue,
    string Source,
    int? SourceLine,
    string Message,
    string Recommendation,
    string Status,
    DateTime DetectedAtUtc,
    string? TreatedBy = null);

public sealed record Mvp0Bypass(
    string BypassId,
    string AnomalyId,
    string ObjectKey,
    string Justification,
    string Decider,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    string? ReplacementValue,
    string Status,
    string RiskLevel,
    double ReliabilityPenalty);

public sealed record Mvp0ReliabilityWeights(
    double Articles,
    double Boms,
    double Routings,
    double Times,
    double Calendars,
    double Trs,
    double BomOpLink,
    double WorkOrderHistory,
    double Freshness,
    double Completeness,
    double Consistency,
    double ValidationRate,
    double BypassPenalty);

public sealed record Mvp0DomainScore(
    string Domain,
    double Score,
    int Checks,
    int Passed,
    int Warnings,
    int Blockings,
    int Bypasses,
    int Missing,
    string Formula);

public sealed record Mvp0InputReliabilityResult(
    double GlobalScore,
    IReadOnlyList<Mvp0DomainScore> Domains,
    string Band,
    string Explanation);

public sealed record Mvp0WorkOrderActual(
    string WoCode,
    string ArticleCode,
    DateOnly PlannedEnd,
    DateOnly ActualEnd,
    double PlannedQty,
    double GoodQty,
    double ScrapQty,
    double PlannedHours,
    double ActualHours,
    string Provenance);

public sealed record Mvp0BacktestItemResult(
    string WoCode,
    string ArticleCode,
    int DateErrorDays,
    double DurationErrorHours,
    double DurationRelativeError,
    double QtyError,
    double ObservedScrapRate,
    string Cause);

public sealed record Mvp0BacktestResult(
    int WoCount,
    int ExploitableCount,
    double MeanDateErrorDays,
    double MedianDateErrorDays,
    double P90DateErrorDays,
    double MeanDurationRelError,
    double MeanQtyRelError,
    double MeanScrapGap,
    double BiasHours,
    double MapeDuration,
    double WithinToleranceRate,
    double CoverageRate,
    IReadOnlyList<Mvp0BacktestItemResult> Items);

public sealed record Mvp0ResultReliabilityResult(
    double GlobalScore,
    double DateScore,
    double DurationScore,
    double QtyScore,
    double ScrapScore,
    double CoverageScore,
    double ExploitableRate,
    string Explanation);

public sealed record Mvp0GateDecision(
    string Outcome,
    string Reason,
    bool RequiresPlanner,
    bool PlannerValidated,
    double InputScore,
    double ResultScore,
    int ActiveBlockings,
    int ActiveBypasses,
    bool HasSimulatedData);

public sealed record Mvp0ArticleRow(string Code, string? Type, string? Unit, string? Family, bool Active, string Provenance, int Line);
public sealed record Mvp0BomRow(string Parent, string Component, double Qty, string? Unit, double ScrapRate, int Line);
public sealed record Mvp0RoutingOpRow(string Article, string OpCode, int Sequence, string? CenterCode, double CycleTime, double SetupTime, int Line);
public sealed record Mvp0CalendarRow(string Code, TimeOnly Start, TimeOnly End, string? Team, double Trs, int Line);

/// <summary>Détection de cycle BOM récursive.</summary>
public static class Mvp0BomCycleDetector
{
    public static IReadOnlyList<string> FindCycles(IReadOnlyList<(string Parent, string Component)> edges)
    {
        var graph = edges
            .GroupBy(e => e.Parent, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Component).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

        var cycles = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        void Dfs(string node)
        {
            if (visiting.Contains(node))
            {
                var i = stack.FindIndex(s => s.Equals(node, StringComparison.OrdinalIgnoreCase));
                cycles.Add(string.Join("→", stack.Skip(i).Append(node)));
                return;
            }

            if (!visited.Add(node)) return;
            visiting.Add(node);
            stack.Add(node);
            if (graph.TryGetValue(node, out var children))
            {
                foreach (var c in children) Dfs(c);
            }

            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(node);
        }

        foreach (var n in graph.Keys.ToList()) Dfs(n);
        return cycles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public static class Mvp0PrecedenceCycleDetector
{
    public static bool HasCycle(IReadOnlyList<(string From, string To)> edges)
        => Mvp0BomCycleDetector.FindCycles(edges.Select(e => (e.From, e.To)).ToList()).Count > 0;
}

public static class Mvp0ValidationEngine
{
    public static IReadOnlyList<Mvp0Anomaly> ValidateArticles(IReadOnlyList<Mvp0ArticleRow> rows, string family)
    {
        var list = new List<Mvp0Anomaly>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in rows)
        {
            if (string.IsNullOrWhiteSpace(a.Code))
                list.Add(Anom("ART_CODE_REQUIRED", Mvp0Severities.Blocking, "ARTICLE", "(empty)", a.Code, "non vide", a.Line, "Code article obligatoire", "Corriger le fichier source"));
            else if (!seen.Add(a.Code))
                list.Add(Anom("ART_CODE_UNIQUE", Mvp0Severities.Blocking, "ARTICLE", a.Code, a.Code, "unique", a.Line, "Code article dupliqué", "Dédoublonner"));
            if (string.IsNullOrWhiteSpace(a.Type))
                list.Add(Anom("ART_TYPE_REQUIRED", Mvp0Severities.Blocking, "ARTICLE", a.Code, a.Type, "FINISHED|COMPONENT|SEMI", a.Line, "Type article manquant", "Renseigner type"));
            if (string.IsNullOrWhiteSpace(a.Unit))
                list.Add(Anom("ART_UNIT_REQUIRED", Mvp0Severities.Blocking, "ARTICLE", a.Code, a.Unit, "unité", a.Line, "Unité manquante", "Renseigner unité"));
            if (string.IsNullOrWhiteSpace(a.Family) || !a.Family.Equals(family, StringComparison.OrdinalIgnoreCase))
                list.Add(Anom("ART_FAMILY_MATCH", Mvp0Severities.Blocking, "ARTICLE", a.Code, a.Family, family, a.Line, "Famille absente ou hors campagne", "Restreindre à la famille pilote"));
            if (!a.Active)
                list.Add(Anom("ART_INACTIVE", Mvp0Severities.Warning, "ARTICLE", a.Code, "false", "true", a.Line, "Article inactif", "Exclure ou réactiver"));
        }

        return list;
    }

    public static IReadOnlyList<Mvp0Anomaly> ValidateBoms(
        IReadOnlyList<Mvp0BomRow> rows,
        IReadOnlySet<string> articles,
        bool requireOpLink,
        IReadOnlySet<string>? linkedBomLines = null)
    {
        var list = new List<Mvp0Anomaly>();
        var edges = new List<(string, string)>();
        var dup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in rows)
        {
            var key = $"{b.Parent}|{b.Component}";
            if (!dup.Add(key))
                list.Add(Anom("BOM_DUP_LINE", Mvp0Severities.Warning, "BOM", key, key, "unique", b.Line, "Ligne BOM en double", "Fusionner"));
            if (!articles.Contains(b.Parent))
                list.Add(Anom("BOM_PARENT_UNKNOWN", Mvp0Severities.Blocking, "BOM", b.Parent, b.Parent, "article connu", b.Line, "Parent inconnu", "Importer l'article"));
            if (!articles.Contains(b.Component))
                list.Add(Anom("BOM_COMP_UNKNOWN", Mvp0Severities.Blocking, "BOM", b.Component, b.Component, "article connu", b.Line, "Composant inconnu", "Importer l'article"));
            if (b.Qty <= 0)
                list.Add(Anom("BOM_QTY_POSITIVE", Mvp0Severities.Blocking, "BOM", key, b.Qty.ToString("F4"), ">0", b.Line, "Quantité non positive", "Corriger qty"));
            if (string.IsNullOrWhiteSpace(b.Unit))
                list.Add(Anom("BOM_UNIT_REQUIRED", Mvp0Severities.Warning, "BOM", key, b.Unit, "unité", b.Line, "Unité BOM manquante", "Renseigner"));
            if (b.ScrapRate is < 0 or > 0.5)
                list.Add(Anom("BOM_SCRAP_RANGE", Mvp0Severities.Warning, "BOM", key, b.ScrapRate.ToString("F3"), "[0;0.5]", b.Line, "Taux rebut hors plage", "Vérifier rebut"));
            edges.Add((b.Parent, b.Component));
            if (requireOpLink && linkedBomLines is not null && !linkedBomLines.Contains(key))
                list.Add(Anom("BOM_OP_LINK_MISSING", Mvp0Severities.Blocking, "BOM", key, "none", "opération liée", b.Line, "Liaison BOM↔opération absente", "Lier à une opération"));
        }

        foreach (var cycle in Mvp0BomCycleDetector.FindCycles(edges))
            list.Add(Anom("BOM_CYCLE", Mvp0Severities.Blocking, "BOM", cycle, cycle, "acyclique", null, "Cycle de nomenclature détecté", "Corriger ou by-pass tracé — CBN interdit sans by-pass"));

        return list;
    }

    public static IReadOnlyList<Mvp0Anomaly> ValidateRoutings(
        IReadOnlyList<Mvp0RoutingOpRow> ops,
        IReadOnlySet<string> articles,
        IReadOnlySet<string> centers,
        double minCycle,
        double maxCycle)
    {
        var list = new List<Mvp0Anomaly>();
        var byArticle = ops.GroupBy(o => o.Article, StringComparer.OrdinalIgnoreCase);
        foreach (var g in byArticle)
        {
            if (!articles.Contains(g.Key))
                list.Add(Anom("RTG_ARTICLE_UNKNOWN", Mvp0Severities.Blocking, "ROUTING", g.Key, g.Key, "article", g.First().Line, "Article gamme inconnu", "Importer article"));
            if (!g.Any())
                list.Add(Anom("RTG_NO_OP", Mvp0Severities.Blocking, "ROUTING", g.Key, "0", ">=1", null, "Gamme sans opération", "Ajouter opérations"));
            var seq = g.Select(x => x.Sequence).OrderBy(x => x).ToList();
            if (seq.Distinct().Count() != seq.Count)
                list.Add(Anom("RTG_SEQ_DUP", Mvp0Severities.Blocking, "ROUTING", g.Key, "dup", "unique", g.First().Line, "Séquences en double", "Corriger séquences"));

            // cycle de précédence simple : seq i -> i+1 edges; detect if op references create cycle (use OpCode chain ordered)
            var ordered = g.OrderBy(x => x.Sequence).Select(x => x.OpCode).ToList();
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                // no backward link assumed; detect if same op repeated as cycle token
            }

            if (ordered.Count != ordered.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                list.Add(Anom("RTG_OP_CYCLE", Mvp0Severities.Blocking, "ROUTING", g.Key, "duplicate-op", "unique-op", g.First().Line, "Cycle/duplication d'opération", "Corriger précédences"));
        }

        foreach (var o in ops)
        {
            if (string.IsNullOrWhiteSpace(o.CenterCode) || !centers.Contains(o.CenterCode))
                list.Add(Anom("RTG_CENTER_UNKNOWN", Mvp0Severities.Blocking, "ROUTING", $"{o.Article}/{o.OpCode}", o.CenterCode, "centre existant", o.Line, "Centre de charge inexistant", "Créer le centre"));
            if (o.CycleTime < 0)
                list.Add(Anom("RTG_CYCLE_NEGATIVE", Mvp0Severities.Blocking, "ROUTING", $"{o.Article}/{o.OpCode}", o.CycleTime.ToString("F3"), ">=0", o.Line, "Temps de cycle négatif", "Corriger"));
            else if (o.CycleTime < minCycle || o.CycleTime > maxCycle)
                list.Add(Anom("RTG_CYCLE_PLAUSIBILITY", Mvp0Severities.Warning, "ROUTING", $"{o.Article}/{o.OpCode}", o.CycleTime.ToString("F3"), $"[{minCycle};{maxCycle}]", o.Line, "Temps hors plage de plausibilité", "Confirmer borne"));
            if (o.SetupTime < 0)
                list.Add(Anom("RTG_SETUP_NEGATIVE", Mvp0Severities.Blocking, "ROUTING", $"{o.Article}/{o.OpCode}", o.SetupTime.ToString("F3"), ">=0", o.Line, "Setup négatif", "Corriger"));
            else if (o.SetupTime == 0)
                list.Add(Anom("RTG_SETUP_MISSING", Mvp0Severities.Warning, "ROUTING", $"{o.Article}/{o.OpCode}", "0", ">0 ou justifié", o.Line, "Setup absent", "Renseigner ou by-pass"));
        }

        return list;
    }

    public static IReadOnlyList<Mvp0Anomaly> ValidateCalendars(IReadOnlyList<Mvp0CalendarRow> rows, bool trsZeroToOne)
    {
        var list = new List<Mvp0Anomaly>();
        var byCode = rows.GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var g in byCode)
        {
            var ordered = g.OrderBy(x => x.Start).ToList();
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                if (ordered[i].End > ordered[i + 1].Start)
                    list.Add(Anom("CAL_OVERLAP", Mvp0Severities.Blocking, "CALENDAR", g.Key, $"{ordered[i].End}>{ordered[i + 1].Start}", "sans chevauchement", ordered[i].Line, "Plages chevauchantes", "Corriger horaires"));
            }
        }

        foreach (var r in rows)
        {
            if (r.End <= r.Start)
                list.Add(Anom("CAL_RANGE", Mvp0Severities.Blocking, "CALENDAR", r.Code, $"{r.Start}-{r.End}", "fin>début", r.Line, "Plage invalide", "Corriger"));
            if (trsZeroToOne)
            {
                if (r.Trs is < 0 or > 1)
                    list.Add(Anom("CAL_TRS_RANGE", Mvp0Severities.Blocking, "CALENDAR", r.Code, r.Trs.ToString("F3"), "[0;1]", r.Line, "TRS hors [0;1]", "Normaliser TRS"));
            }
            else if (r.Trs is < 0 or > 100)
                list.Add(Anom("CAL_TRS_RANGE", Mvp0Severities.Blocking, "CALENDAR", r.Code, r.Trs.ToString("F3"), "[0;100]", r.Line, "TRS hors [0;100]", "Normaliser"));
        }

        return list;
    }

    private static Mvp0Anomaly Anom(
        string rule, string sev, string type, string key, string? found, string? expected, int? line,
        string msg, string reco)
        => new(
            $"{rule}:{key}:{line}",
            rule, sev, type, key, found, expected, "MVP0_IMPORT", line, msg, reco, "OPEN", DateTime.UtcNow);
}

public static class Mvp0FreshnessEngine
{
    public static string Band(DateTime? asOfUtc, DateTime nowUtc, int agingDays, int staleDays)
    {
        if (asOfUtc is null) return Mvp0FreshnessBands.Unknown;
        var age = (nowUtc - asOfUtc.Value).TotalDays;
        if (age <= agingDays) return Mvp0FreshnessBands.Fresh;
        if (age <= staleDays) return Mvp0FreshnessBands.Aging;
        return Mvp0FreshnessBands.Stale;
    }
}

public static class Mvp0DataReliabilityEngine
{
    public static Mvp0InputReliabilityResult Compute(
        IReadOnlyList<Mvp0Anomaly> anomalies,
        IReadOnlyList<Mvp0Bypass> activeBypasses,
        Mvp0ReliabilityWeights weights,
        IReadOnlyDictionary<string, int> checksByDomain,
        DateTime? freshestUtc,
        DateTime nowUtc,
        int agingDays,
        int staleDays)
    {
        var bypassedRules = activeBypasses.Select(b => b.AnomalyId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Mvp0DomainScore ScoreDomain(string domain, string[] rulePrefixes, double weight)
        {
            checksByDomain.TryGetValue(domain, out var checks);
            checks = Math.Max(checks, 1);
            var domainAnoms = anomalies.Where(a => rulePrefixes.Any(p => a.RuleCode.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();
            var block = domainAnoms.Count(a => a.Severity == Mvp0Severities.Blocking && !bypassedRules.Contains(a.AnomalyId));
            var warn = domainAnoms.Count(a => a.Severity == Mvp0Severities.Warning);
            var bypassed = domainAnoms.Count(a => bypassedRules.Contains(a.AnomalyId));
            var passed = Math.Max(0, checks - domainAnoms.Count);
            var penalty = block * 25 + warn * 5 + bypassed * 8;
            var score = Math.Clamp(100.0 - penalty, 0, 100);
            var formula = $"100 - (BLOCK*{block}*25 + WARN*{warn}*5 + BYPASS*{bypassed}*8) ; checks={checks} passed≈{passed}";
            return new Mvp0DomainScore(domain, score, checks, passed, warn, block, bypassed, 0, formula);
        }

        var domains = new List<Mvp0DomainScore>
        {
            ScoreDomain("Articles", ["ART_"], weights.Articles),
            ScoreDomain("Nomenclatures", ["BOM_"], weights.Boms),
            ScoreDomain("Gammes", ["RTG_"], weights.Routings),
            ScoreDomain("Temps", ["RTG_CYCLE", "RTG_SETUP"], weights.Times),
            ScoreDomain("Calendriers", ["CAL_"], weights.Calendars),
            ScoreDomain("TRS", ["CAL_TRS"], weights.Trs),
            ScoreDomain("LiaisonBOM-OP", ["BOM_OP"], weights.BomOpLink),
            ScoreDomain("HistoriqueOF", ["WO_"], weights.WorkOrderHistory)
        };

        var freshness = Mvp0FreshnessEngine.Band(freshestUtc, nowUtc, agingDays, staleDays);
        var freshScore = freshness switch
        {
            Mvp0FreshnessBands.Fresh => 100,
            Mvp0FreshnessBands.Aging => 70,
            Mvp0FreshnessBands.Stale => 40,
            _ => 50
        };
        domains.Add(new Mvp0DomainScore("Fraicheur", freshScore, 1, freshness == Mvp0FreshnessBands.Fresh ? 1 : 0, 0, 0, 0, 0,
            $"band={freshness} agingDays={agingDays} staleDays={staleDays}"));

        var totalWeight = weights.Articles + weights.Boms + weights.Routings + weights.Times + weights.Calendars
                          + weights.Trs + weights.BomOpLink + weights.WorkOrderHistory + weights.Freshness;
        var weighted =
            domains[0].Score * weights.Articles +
            domains[1].Score * weights.Boms +
            domains[2].Score * weights.Routings +
            domains[3].Score * weights.Times +
            domains[4].Score * weights.Calendars +
            domains[5].Score * weights.Trs +
            domains[6].Score * weights.BomOpLink +
            domains[7].Score * weights.WorkOrderHistory +
            freshScore * weights.Freshness;
        var global = totalWeight <= 0 ? 0 : weighted / totalWeight;
        var bypassPenalty = activeBypasses.Sum(b => b.ReliabilityPenalty);
        global = Math.Clamp(global - bypassPenalty, 0, 100);

        var band = global >= 85 ? "GO_CANDIDATE" : global >= 70 ? "GO_WITH_RESERVATIONS_CANDIDATE" : "NO_GO_CANDIDATE";
        return new Mvp0InputReliabilityResult(Math.Round(global, 2), domains, band,
            $"global = Σ(score_d * poids_d)/Σpoids − pénalité_bypass({bypassPenalty:F1}) = {global:F2}");
    }
}

public static class Mvp0BacktestReliabilityEngine
{
    public static Mvp0BacktestResult Run(
        IReadOnlyList<Mvp0WorkOrderActual> orders,
        int dateToleranceDays,
        double durationToleranceRel)
    {
        var items = new List<Mvp0BacktestItemResult>();
        foreach (var o in orders.OrderBy(x => x.WoCode, StringComparer.Ordinal))
        {
            if (o.Provenance == Mvp0Provenance.Simulated)
            {
                // still included but marked
            }

            var dateErr = Math.Abs(o.ActualEnd.DayNumber - o.PlannedEnd.DayNumber);
            var durErr = o.ActualHours - o.PlannedHours;
            var durRel = o.PlannedHours == 0 ? (o.ActualHours == 0 ? 0 : 1) : Math.Abs(durErr) / o.PlannedHours;
            var qtyErr = o.GoodQty - o.PlannedQty;
            var scrapObs = (o.GoodQty + o.ScrapQty) <= 0 ? 0 : o.ScrapQty / (o.GoodQty + o.ScrapQty);
            items.Add(new Mvp0BacktestItemResult(
                o.WoCode, o.ArticleCode, dateErr, durErr, durRel, qtyErr, scrapObs, "UNKNOWN"));
        }

        if (items.Count == 0)
        {
            return new Mvp0BacktestResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        }

        var dates = items.Select(i => (double)i.DateErrorDays).OrderBy(x => x).ToList();
        double Quantile(IReadOnlyList<double> xs, double p)
        {
            if (xs.Count == 0) return 0;
            var idx = (int)Math.Clamp(Math.Ceiling(p * xs.Count) - 1, 0, xs.Count - 1);
            return xs[idx];
        }

        var within = items.Count(i => i.DateErrorDays <= dateToleranceDays && i.DurationRelativeError <= durationToleranceRel);
        var mape = items.Average(i => i.DurationRelativeError) * 100;
        return new Mvp0BacktestResult(
            orders.Count,
            items.Count,
            items.Average(i => i.DateErrorDays),
            Quantile(dates, 0.5),
            Quantile(dates, 0.9),
            items.Average(i => i.DurationRelativeError),
            items.Average(i => Math.Abs(i.QtyError) / Math.Max(1, orders.First(o => o.WoCode == i.WoCode).PlannedQty)),
            items.Average(i => i.ObservedScrapRate),
            items.Average(i => i.DurationErrorHours),
            mape,
            within / (double)items.Count,
            items.Count / (double)Math.Max(1, orders.Count),
            items);
    }

    public static Mvp0ResultReliabilityResult Score(Mvp0BacktestResult bt)
    {
        if (bt.ExploitableCount == 0)
        {
            return new Mvp0ResultReliabilityResult(0, 0, 0, 0, 0, 0, 0, "INSUFFICIENT_DATA — aucun OF exploitable");
        }

        double S(double err, double scale) => Math.Clamp(100 - err * scale, 0, 100);
        var date = S(bt.MeanDateErrorDays, 8);
        var dur = S(bt.MeanDurationRelError * 100, 1.2);
        var qty = S(bt.MeanQtyRelError * 100, 1.5);
        var scrap = S(bt.MeanScrapGap * 100, 2);
        var cov = bt.CoverageRate * 100;
        var global = (date + dur + qty + scrap + cov + bt.WithinToleranceRate * 100) / 6.0;
        return new Mvp0ResultReliabilityResult(
            Math.Round(global, 2), date, dur, qty, scrap, cov, bt.ExploitableCount / (double)Math.Max(1, bt.WoCount),
            $"moyenne(date={date:F0}, dur={dur:F0}, qty={qty:F0}, scrap={scrap:F0}, cov={cov:F0}, tol={bt.WithinToleranceRate:P0})");
    }
}

public static class Mvp0GateDecisionEngine
{
    public static Mvp0GateDecision Decide(
        Mvp0InputReliabilityResult input,
        Mvp0ResultReliabilityResult result,
        IReadOnlyList<Mvp0Anomaly> anomalies,
        IReadOnlyList<Mvp0Bypass> activeBypasses,
        bool plannerValidated,
        bool anySimulatedData,
        double goThreshold,
        double goResThreshold)
    {
        var blockings = anomalies.Count(a =>
            a.Severity == Mvp0Severities.Blocking
            && a.Status == "OPEN"
            && !activeBypasses.Any(b => b.AnomalyId.Equals(a.AnomalyId, StringComparison.OrdinalIgnoreCase) && b.Status == "ACTIVE"));

        if (anySimulatedData)
        {
            return new Mvp0GateDecision(Mvp0GateOutcomes.InsufficientData,
                "Données SIMULATED présentes — GO final interdit.", true, plannerValidated,
                input.GlobalScore, result.GlobalScore, blockings, activeBypasses.Count, true);
        }

        if (!plannerValidated)
        {
            return new Mvp0GateDecision(Mvp0GateOutcomes.InsufficientData,
                "Avis planificateur obligatoire non saisi — décision finale non prise par le système seul.",
                true, false, input.GlobalScore, result.GlobalScore, blockings, activeBypasses.Count, false);
        }

        if (blockings > 0)
        {
            return new Mvp0GateDecision(Mvp0GateOutcomes.NoGo,
                $"{blockings} anomalie(s) BLOCKING actives non by-passées.", true, true,
                input.GlobalScore, result.GlobalScore, blockings, activeBypasses.Count, false);
        }

        if (input.GlobalScore >= goThreshold && result.GlobalScore >= goThreshold && activeBypasses.Count == 0)
        {
            return new Mvp0GateDecision(Mvp0GateOutcomes.Go,
                "Scores ≥ seuil GO, aucun bloquant, aucun by-pass, planificateur OK.", true, true,
                input.GlobalScore, result.GlobalScore, 0, 0, false);
        }

        if (input.GlobalScore >= goResThreshold && result.GlobalScore >= goResThreshold)
        {
            return new Mvp0GateDecision(Mvp0GateOutcomes.GoWithReservations,
                "Scores dans bande GO_WITH_RESERVATIONS ou by-pass actifs — poursuivre MVP-1 sous réserves.",
                true, true, input.GlobalScore, result.GlobalScore, blockings, activeBypasses.Count, false);
        }

        return new Mvp0GateDecision(Mvp0GateOutcomes.NoGo,
            "Scores sous seuils configurés.", true, true,
            input.GlobalScore, result.GlobalScore, blockings, activeBypasses.Count, false);
    }
}

public static class Mvp0ImportGuard
{
    /// <summary>Toute ligne inconnue / rejetée doit apparaître — jamais silence.</summary>
    public static (IReadOnlyList<T> Accepted, IReadOnlyList<string> Rejects) Partition<T>(
        IReadOnlyList<(T? Row, string? Error, int Line)> parsed)
    {
        var ok = new List<T>();
        var rejects = new List<string>();
        foreach (var p in parsed)
        {
            if (p.Row is null || !string.IsNullOrWhiteSpace(p.Error))
                rejects.Add($"Ligne {p.Line}: {p.Error ?? "ligne inconnue rejetée"}");
            else
                ok.Add(p.Row);
        }

        return (ok, rejects);
    }
}

public static class Mvp0BypassRules
{
    public static Mvp0Bypass Create(
        string anomalyId,
        string objectKey,
        string justification,
        string decider,
        string riskLevel,
        double penalty,
        string? replacement = null,
        DateTime? expires = null)
    {
        if (string.IsNullOrWhiteSpace(justification))
            throw new InvalidOperationException("By-pass refuse : justification obligatoire.");
        if (string.IsNullOrWhiteSpace(decider))
            throw new InvalidOperationException("By-pass refuse : décideur obligatoire.");

        return new Mvp0Bypass(
            "BP-" + Guid.NewGuid().ToString("N")[..8],
            anomalyId, objectKey, justification.Trim(), decider, DateTime.UtcNow, expires,
            replacement, "ACTIVE", riskLevel, penalty);
    }
}

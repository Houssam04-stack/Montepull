namespace Axioplan.GammesNomenclatures.Domain.Aps.Ctp;

public static class ApsRegimes
{
    public const string Mts = "MTS";
    public const string Mto = "MTO";
}

public static class ApsCtpOutcomes
{
    public const string FeasibleInternal = "FAISABLE_INTERNE";
    public const string FeasibleSubcontract = "FAISABLE_SOUS_TRAITANCE";
    public const string FeasibleOtherDate = "FAISABLE_AUTRE_DATE";
    public const string Infeasible = "INFAISABLE";
}

public static class ApsFloatStatuses
{
    public const string Rigide = "RIGIDE";
    public const string Faible = "FAIBLE";
    public const string Moyen = "MOYEN";
    public const string Eleve = "ELEVE";
}

public sealed record ApsFloatThresholds(
    int FaibleMaxDays = 3,
    int MoyenMaxDays = 10);

public sealed record ApsRegimeDefaultRule(
    string? ArticleCategory,
    string? CustomerCategory,
    string? ArticleCode,
    string? CustomerCode,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string Regime,
    int SpecificityRank);

public sealed record ApsCtpDemand(
    string DemandId,
    string ArticleCode,
    string CustomerCode,
    string? ArticleCategory,
    string? CustomerCategory,
    double Quantity,
    DateOnly DesiredDate,
    DateOnly? CustomerMaxDate,
    double? Margin,
    int Priority,
    string? DeclaredRegime,
    double? QuantityTolerance,
    string? OrderReference);

public sealed record ApsCtpConstraintCause(
    string ConstraintType,
    string? ResourceOrMaterial,
    DateOnly? WindowFrom,
    DateOnly? WindowTo,
    double? Charge,
    double? Capacity,
    double? Shortage,
    IReadOnlyList<string> OptionsTried,
    IReadOnlyList<string> OptionsExhausted,
    string Explanation);

public sealed record ApsReliabilityResult(
    double? Value,
    string ConfidenceLevel,
    IReadOnlyList<string> SourcesUsed,
    IReadOnlyList<string> MissingParameters,
    string Status);

public sealed record ApsCtpHorizonTrace(
    DateOnly HorizonDate,
    bool Passed,
    string Reason,
    string? Bottleneck);

public sealed record ApsCtpAnswer(
    string DemandId,
    string Outcome,
    DateOnly? ProposedDate,
    string EffectiveRegime,
    string Bottleneck,
    ApsReliabilityResult Reliability,
    double? Hre,
    string HreStatus,
    double? MarginDensity,
    string DensityStatus,
    int? FloatDays,
    string FloatStatus,
    string? CircuitCode,
    string? SelectedBath,
    string? ExternalEngagementCode,
    bool ReservesCreated,
    ApsCtpConstraintCause Cause,
    IReadOnlyList<ApsCtpHorizonTrace> HorizonChronology,
    IReadOnlyList<string> OptionsTried,
    DateOnly? FirstPotentialDate,
    string? EscalateToM2Recommendation);

/// <summary>Résolution régime par défauts hiérarchisés (plus spécifique gagne).</summary>
public static class RegimeResolutionEngine
{
    public static string Resolve(
        ApsCtpDemand demand,
        IReadOnlyList<ApsRegimeDefaultRule> rules,
        DateOnly asOf)
    {
        if (!string.IsNullOrWhiteSpace(demand.DeclaredRegime))
        {
            return demand.DeclaredRegime!;
        }

        var matches = rules
            .Where(r => Matches(r, demand, asOf))
            .OrderByDescending(r => Specificity(r))
            .ThenByDescending(r => r.SpecificityRank)
            .ToList();

        return matches.Count > 0 ? matches[0].Regime : ApsRegimes.Mto; // défaut TO_CONFIRM: MTO
    }

    private static bool Matches(ApsRegimeDefaultRule r, ApsCtpDemand d, DateOnly asOf)
    {
        if (r.PeriodFrom is not null && asOf < r.PeriodFrom) return false;
        if (r.PeriodTo is not null && asOf > r.PeriodTo) return false;
        if (r.ArticleCode is not null && !r.ArticleCode.Equals(d.ArticleCode, StringComparison.OrdinalIgnoreCase)) return false;
        if (r.CustomerCode is not null && !r.CustomerCode.Equals(d.CustomerCode, StringComparison.OrdinalIgnoreCase)) return false;
        if (r.ArticleCategory is not null && !r.ArticleCategory.Equals(d.ArticleCategory ?? "", StringComparison.OrdinalIgnoreCase)) return false;
        if (r.CustomerCategory is not null && !r.CustomerCategory.Equals(d.CustomerCategory ?? "", StringComparison.OrdinalIgnoreCase)) return false;
        return r.ArticleCode is not null || r.CustomerCode is not null
               || r.ArticleCategory is not null || r.CustomerCategory is not null;
    }

    private static int Specificity(ApsRegimeDefaultRule r)
    {
        // 4 article×client×période ; 3 cat×client ; 2 article×cat client ; 1 cat×cat
        var hasArt = r.ArticleCode is not null;
        var hasCust = r.CustomerCode is not null;
        var hasArtCat = r.ArticleCategory is not null;
        var hasCustCat = r.CustomerCategory is not null;
        var hasPeriod = r.PeriodFrom is not null || r.PeriodTo is not null;
        if (hasArt && hasCust && hasPeriod) return 4;
        if (hasArt && hasCust) return 4;
        if (hasArtCat && hasCust) return 3;
        if (hasArt && hasCustCat) return 2;
        if (hasArtCat && hasCustCat) return 1;
        return 0;
    }
}

public sealed record ApsCtpRouterDecision(
    string Route, // R1 | R2 | R3
    string EffectiveRegime,
    bool PfStockSufficient,
    bool YarnAvailable,
    bool RequiresYarnPurchase,
    IReadOnlyList<string> Traces);

public static class CtpRouterEngine
{
    public static ApsCtpRouterDecision Route(
        string effectiveRegime,
        double pfAvailableLibre,
        double demandQty,
        bool yarnBathAvailable,
        bool yarnPurchaseRequired)
    {
        var traces = new List<string>();
        if (effectiveRegime.Equals(ApsRegimes.Mts, StringComparison.OrdinalIgnoreCase)
            || string.Equals(effectiveRegime, "R1", StringComparison.OrdinalIgnoreCase))
        {
            if (pfAvailableLibre >= demandQty)
            {
                traces.Add($"R1 MTS : ATP PF {pfAvailableLibre:F2} ≥ besoin {demandQty:F2} → promesse immédiate.");
                return new ApsCtpRouterDecision("R1", ApsRegimes.Mts, true, yarnBathAvailable, false, traces);
            }

            traces.Add($"R1 insuffisant (PF {pfAvailableLibre:F2} < {demandQty:F2}) → bascule MTO.");
            effectiveRegime = ApsRegimes.Mto;
        }

        if (yarnBathAvailable && !yarnPurchaseRequired)
        {
            traces.Add("R2 MTO : fil bain-compatible disponible → calcul capacité.");
            return new ApsCtpRouterDecision("R2", ApsRegimes.Mto, false, true, false, traces);
        }

        traces.Add("R3 MTO : achat fil requis — vérifier délai fournisseur / teinture / traversée / contrôle.");
        return new ApsCtpRouterDecision("R3", ApsRegimes.Mto, false, false, true, traces);
    }
}

public static class ReliabilityEngine
{
    public static ApsReliabilityResult ForR1Stock()
        => new(0.99, "ELEVEE", ["ATP_PF"], [], "OK");

    public static ApsReliabilityResult ForR2Internal(double? resourceSigma)
    {
        if (resourceSigma is null)
        {
            return new ApsReliabilityResult(null, "TO_CONFIRM", [], ["sigma_ressource"], "TO_CONFIRM");
        }

        // Déterministe simple : plus sigma élevé, fiabilité plus basse (bornée).
        var v = Math.Clamp(1.0 - resourceSigma.Value, 0.5, 0.98);
        return new ApsReliabilityResult(v, "MOYENNE", ["sigma_ressource"], [], "OK");
    }

    public static ApsReliabilityResult ForR3Supplier(double? muDays, double? sigmaDays)
    {
        var missing = new List<string>();
        if (muDays is null) missing.Add("mu_fournisseur");
        if (sigmaDays is null) missing.Add("sigma_fournisseur");
        if (missing.Count > 0)
        {
            return new ApsReliabilityResult(null, "TO_CONFIRM", [], missing, "TO_CONFIRM");
        }

        var v = Math.Clamp(1.0 - (sigmaDays!.Value / Math.Max(1, muDays!.Value + sigmaDays.Value)), 0.4, 0.9);
        return new ApsReliabilityResult(v, "BASSE", ["mu_fournisseur", "sigma_fournisseur"], [], "OK");
    }

    public static ApsReliabilityResult ForSubcontract(double? partnerReliability, double? reworkRate)
    {
        if (partnerReliability is null)
        {
            return new ApsReliabilityResult(null, "TO_CONFIRM", [], ["fiabilite_partenaire"], "TO_CONFIRM");
        }

        var rework = reworkRate ?? 0;
        var v = Math.Clamp(partnerReliability.Value * (1 - rework), 0.3, 0.95);
        var missing = reworkRate is null ? new List<string> { "taux_retouche" } : [];
        return new ApsReliabilityResult(v, missing.Count > 0 ? "TO_CONFIRM" : "MOYENNE",
            ["fiabilite_partenaire"], missing, missing.Count > 0 ? "TO_CONFIRM" : "OK");
    }
}

public sealed record ApsCtpResourceDay(
    string ResourceCode,
    DateOnly Day,
    double CapEngageable,
    double RhoTarget,
    double AlreadyLoaded);

public sealed record ApsCtpExternalSlot(
    string EngagementCode,
    string PartnerCode,
    double ResidualVolume,
    DateOnly HandoverDate,
    DateOnly ReturnDate,
    DateOnly EngagementDeadline,
    string Status,
    double? PartnerReliability = null,
    double? ReworkRate = null);

public sealed record ApsCtpYarnState(
    bool CompatibleBathAvailable,
    string? BathCode,
    double AvailableQty,
    DateOnly? AvailableFrom,
    bool PurchaseRequired,
    double? SupplierMuDays,
    double? SupplierSigmaDays,
    double PurchaseLeadDays);

public sealed record ApsCtpEvaluationContext(
    ApsCtpDemand Demand,
    string EffectiveRegime,
    string Route,
    double PfAvailable,
    ApsCtpYarnState Yarn,
    IReadOnlyList<ApsCtpResourceDay> CapacityDays,
    IReadOnlyList<ApsCtpExternalSlot> ExternalEngagements,
    string PrimaryCircuit,
    string? AlternateCircuit,
    string BottleneckResourceCode,
    double DemandLoadOnBottleneck,
    double? HrePerUnit,
    string HreStatus,
    double? ResourceSigma,
    DateOnly Today,
    int MaxHorizonDays,
    ApsFloatThresholds FloatThresholds,
    bool AllowMtsArbitration = false);

/// <summary>
/// Point fixe déterministe sur l'horizon — sans solveur.
/// </summary>
public static class CtpEngine
{
    public static ApsCtpAnswer Evaluate(ApsCtpEvaluationContext ctx, bool commitReservations)
    {
        var optionsTried = new List<string>();
        var chronology = new List<ApsCtpHorizonTrace>();
        var demand = ctx.Demand;
        var maxDate = demand.CustomerMaxDate ?? demand.DesiredDate.AddDays(ctx.MaxHorizonDays);

        // --- R1 ---
        if (ctx.Route == "R1" && ctx.PfAvailable >= demand.Quantity)
        {
            optionsTried.Add("R1_ATP_PF");
            var hre = ctx.HrePerUnit is null ? (double?)null : ctx.HrePerUnit.Value * demand.Quantity;
            var (dens, densStatus) = Density(demand.Margin, hre, ctx.HreStatus);
            var floatDays = FloatDays(demand.DesiredDate, maxDate, demand.CustomerMaxDate, ctx.FloatThresholds, out var floatStatus);
            return new ApsCtpAnswer(
                demand.DemandId,
                ApsCtpOutcomes.FeasibleInternal,
                demand.DesiredDate,
                ApsRegimes.Mts,
                "STOCK_PF",
                ReliabilityEngine.ForR1Stock(),
                hre,
                ctx.HreStatus,
                dens,
                densStatus,
                floatDays,
                floatStatus,
                ctx.PrimaryCircuit,
                null,
                null,
                commitReservations,
                new ApsCtpConstraintCause(
                    "STOCK_PF", "PF", demand.DesiredDate, demand.DesiredDate,
                    null, ctx.PfAvailable, null, optionsTried, [],
                    $"Promesse immédiate R1 : stock PF disponible ({ctx.PfAvailable:F2})."),
                [new ApsCtpHorizonTrace(demand.DesiredDate, true, "R1 ATP PF OK", "STOCK_PF")],
                optionsTried,
                demand.DesiredDate,
                null);
        }

        if (ctx.Route == "R1")
        {
            optionsTried.Add("R1_INSUFFISANT_BASCULE_MTO");
        }

        // RESERVE_MTS : pas de conso silencieuse
        if (!ctx.AllowMtsArbitration && ctx.Yarn.CompatibleBathAvailable == false && ctx.PfAvailable > 0)
        {
            // informational only
        }

        DateOnly? firstPotential = null;
        ApsCtpAnswer? internalHit = null;

        // Recherche interne puis HS (palier déjà dans CapEngageable si inclus), puis externe
        for (var offset = 0; offset <= ctx.MaxHorizonDays; offset++)
        {
            var h = ctx.Today.AddDays(offset);
            if (h < MinHorizonForRoute(ctx))
            {
                chronology.Add(new ApsCtpHorizonTrace(h, false, "Sous horizon minimal route", null));
                continue;
            }

            if (demand.CustomerMaxDate is not null && h > demand.CustomerMaxDate)
            {
                chronology.Add(new ApsCtpHorizonTrace(h, false, "Dépasse date max client", null));
                break;
            }

            optionsTried.Add($"HORIZON_{h:yyyy-MM-dd}_INTERNE");
            var check = CheckCapacityAndYarn(ctx, h, useExternal: false, optionsTried);
            chronology.Add(new ApsCtpHorizonTrace(h, check.Ok, check.Reason, check.Bottleneck));
            if (!check.Ok)
            {
                continue;
            }

            firstPotential ??= h;
            internalHit = BuildFeasible(ctx, h, ApsCtpOutcomes.FeasibleInternal,
                h == demand.DesiredDate ? ApsCtpOutcomes.FeasibleInternal : ApsCtpOutcomes.FeasibleOtherDate,
                check, optionsTried, chronology, commitReservations, null);
            break;
        }

        if (internalHit is not null)
        {
            // Si date > souhaitée → FAISABLE_AUTRE_DATE
            if (internalHit.ProposedDate > demand.DesiredDate)
            {
                return internalHit with { Outcome = ApsCtpOutcomes.FeasibleOtherDate };
            }

            return internalHit;
        }

        // Engagements externes
        foreach (var eng in ctx.ExternalEngagements
                     .Where(e => string.Equals(e.Status, "OPEN", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.ReturnDate)
                     .ThenBy(e => e.EngagementCode, StringComparer.Ordinal))
        {
            optionsTried.Add($"EXTERNE_{eng.EngagementCode}");
            if (eng.ResidualVolume < demand.Quantity)
            {
                optionsTried.Add($"EXTERNE_{eng.EngagementCode}_VOLUME_INSUFFISANT");
                continue;
            }

            if (eng.EngagementDeadline < ctx.Today)
            {
                optionsTried.Add($"EXTERNE_{eng.EngagementCode}_DEADLINE_PASSEE");
                continue;
            }

            var returnDay = eng.ReturnDate;
            if (demand.CustomerMaxDate is not null && returnDay > demand.CustomerMaxDate)
            {
                optionsTried.Add($"EXTERNE_{eng.EngagementCode}_RETOUR_TROP_TARD");
                continue;
            }

            if (eng.HandoverDate < ctx.Today)
            {
                continue;
            }

            chronology.Add(new ApsCtpHorizonTrace(returnDay, true,
                $"Engagement {eng.EngagementCode} volume {eng.ResidualVolume:F0}", eng.PartnerCode));
            firstPotential ??= returnDay;
            var outcome = returnDay == demand.DesiredDate
                ? ApsCtpOutcomes.FeasibleSubcontract
                : ApsCtpOutcomes.FeasibleSubcontract;
            if (returnDay > demand.DesiredDate)
            {
                // still subcontract but other date semantics via cause
            }

            var check = new HorizonCheck(true,
                $"Sous-traitance retenue car l'engagement {eng.EngagementCode} couvre {eng.ResidualVolume:F0} pièces.",
                eng.PartnerCode, null, eng.ResidualVolume, null);
            return BuildFeasible(ctx, returnDay, outcome, outcome, check, optionsTried, chronology,
                commitReservations, eng.EngagementCode, eng);
        }

        optionsTried.Add("EXTERNE_ABSENT_OU_INCOMPATIBLE");

        // Infaisable
        var cause = new ApsCtpConstraintCause(
            "INFAISABLE",
            ctx.BottleneckResourceCode,
            ctx.Today,
            maxDate,
            ctx.DemandLoadOnBottleneck,
            null,
            ctx.Yarn.AvailableQty < demand.Quantity ? demand.Quantity - ctx.Yarn.AvailableQty : null,
            optionsTried,
            optionsTried,
            firstPotential is null
                ? "Infaisable : fil indisponible et aucun engagement externe compatible (ou capacité insuffisante)."
                : $"Infaisable dans les contraintes client ; première date potentielle {firstPotential:yyyy-MM-dd} hors limite.");

        return new ApsCtpAnswer(
            demand.DemandId,
            ApsCtpOutcomes.Infeasible,
            null,
            ctx.EffectiveRegime,
            ctx.BottleneckResourceCode,
            ReliabilityEngine.ForR2Internal(ctx.ResourceSigma),
            ctx.HrePerUnit is null ? null : ctx.HrePerUnit * demand.Quantity,
            ctx.HreStatus,
            Density(demand.Margin, ctx.HrePerUnit is null ? null : ctx.HrePerUnit * demand.Quantity, ctx.HreStatus).density,
            Density(demand.Margin, ctx.HrePerUnit is null ? null : ctx.HrePerUnit * demand.Quantity, ctx.HreStatus).status,
            null,
            ApsFloatStatuses.Rigide,
            ctx.PrimaryCircuit,
            ctx.Yarn.BathCode,
            null,
            false,
            cause,
            chronology,
            optionsTried,
            firstPotential,
            "Remonter au M2 : arbitrage capacité / engagement / date client.");
    }

    private static DateOnly MinHorizonForRoute(ApsCtpEvaluationContext ctx)
    {
        if (ctx.Route != "R3")
        {
            return ctx.Today;
        }

        var lead = ctx.Yarn.PurchaseLeadDays;
        if (ctx.Yarn.SupplierMuDays is not null)
        {
            lead = Math.Max(lead, ctx.Yarn.SupplierMuDays.Value);
        }

        return ctx.Today.AddDays((int)Math.Ceiling(lead));
    }

    private sealed record HorizonCheck(
        bool Ok,
        string Reason,
        string? Bottleneck,
        double? Charge,
        double? Capacity,
        double? Shortage);

    private static HorizonCheck CheckCapacityAndYarn(
        ApsCtpEvaluationContext ctx,
        DateOnly h,
        bool useExternal,
        List<string> optionsTried)
    {
        if (ctx.Route is "R2" or "R3")
        {
            if (ctx.Route == "R2" && !ctx.Yarn.CompatibleBathAvailable)
            {
                return new HorizonCheck(false, "ATP fil bain-compatible insuffisant.", "FIL",
                    null, ctx.Yarn.AvailableQty, ctx.Demand.Quantity - ctx.Yarn.AvailableQty);
            }

            if (ctx.Yarn.AvailableFrom is not null && h < ctx.Yarn.AvailableFrom)
            {
                return new HorizonCheck(false,
                    $"Date proposée car bain compatible disponible seulement à {ctx.Yarn.AvailableFrom:yyyy-MM-dd}.",
                    "FIL", null, ctx.Yarn.AvailableQty, null);
            }
        }

        // Cap_cum et charge jusqu'à h sur goulot
        var days = ctx.CapacityDays
            .Where(d => d.ResourceCode.Equals(ctx.BottleneckResourceCode, StringComparison.OrdinalIgnoreCase)
                        && d.Day >= ctx.Today && d.Day <= h)
            .OrderBy(d => d.Day)
            .ToList();

        if (days.Count == 0)
        {
            return new HorizonCheck(false, "Aucune capacité sur la fenêtre.", ctx.BottleneckResourceCode, null, 0, null);
        }

        var capCum = days.Sum(d => d.CapEngageable);
        var loadCum = days.Sum(d => d.AlreadyLoaded) + ctx.DemandLoadOnBottleneck;
        if (capCum <= 0)
        {
            return new HorizonCheck(false, "Cap_cum=0 — saturation non calculable / infinie.", ctx.BottleneckResourceCode,
                loadCum, 0, null);
        }

        var rho = loadCum / capCum;
        var rhoTarget = days.Average(d => d.RhoTarget);
        if (rho > rhoTarget)
        {
            return new HorizonCheck(false,
                $"Date proposée plus tard car {ctx.BottleneckResourceCode} dépasse rho_cible (rho={rho:F3} > {rhoTarget:F3}) à l'horizon {h:yyyy-MM-dd}.",
                ctx.BottleneckResourceCode, loadCum, capCum, loadCum - rhoTarget * capCum);
        }

        optionsTried.Add($"RHO_OK_{h:yyyy-MM-dd}_{rho:F3}");
        return new HorizonCheck(true,
            $"Faisable en interne à {h:yyyy-MM-dd} (rho={rho:F3} ≤ rho_cible={rhoTarget:F3}).",
            ctx.BottleneckResourceCode, loadCum, capCum, null);
    }

    private static ApsCtpAnswer BuildFeasible(
        ApsCtpEvaluationContext ctx,
        DateOnly h,
        string outcome,
        string outcomeFinal,
        HorizonCheck check,
        List<string> optionsTried,
        List<ApsCtpHorizonTrace> chronology,
        bool commit,
        string? externalCode,
        ApsCtpExternalSlot? eng = null)
    {
        var demand = ctx.Demand;
        var hre = ctx.HrePerUnit is null ? (double?)null : ctx.HrePerUnit.Value * demand.Quantity;
        var (dens, densStatus) = Density(demand.Margin, hre, ctx.HreStatus);
        var maxAcceptable = demand.CustomerMaxDate ?? h.AddDays(ctx.MaxHorizonDays);
        // date_max : sans violer max client ; simplifié = maxAcceptable borné
        var dateMax = demand.CustomerMaxDate is not null && demand.CustomerMaxDate < maxAcceptable
            ? demand.CustomerMaxDate.Value
            : maxAcceptable;
        if (externalCode is not null && eng is not null && eng.EngagementDeadline < dateMax)
        {
            dateMax = eng.EngagementDeadline;
        }

        var floatDays = Math.Max(0, dateMax.DayNumber - h.DayNumber);
        var floatStatus = ClassifyFloat(floatDays, ctx.FloatThresholds);

        ApsReliabilityResult reliability = externalCode is not null && eng is not null
            ? ReliabilityEngine.ForSubcontract(eng.PartnerReliability, eng.ReworkRate)
            : ctx.Route == "R3"
                ? ReliabilityEngine.ForR3Supplier(ctx.Yarn.SupplierMuDays, ctx.Yarn.SupplierSigmaDays)
                : ReliabilityEngine.ForR2Internal(ctx.ResourceSigma);

        var actualOutcome = h > demand.DesiredDate && externalCode is null
            ? ApsCtpOutcomes.FeasibleOtherDate
            : outcomeFinal;

        return new ApsCtpAnswer(
            demand.DemandId,
            actualOutcome,
            h,
            ctx.EffectiveRegime,
            check.Bottleneck ?? ctx.BottleneckResourceCode,
            reliability,
            hre,
            ctx.HreStatus,
            dens,
            densStatus,
            floatDays,
            floatStatus,
            externalCode is null ? ctx.PrimaryCircuit : "CIR_ST_EXT",
            ctx.Yarn.BathCode,
            externalCode,
            commit,
            new ApsCtpConstraintCause(
                externalCode is null ? "CAPACITE" : "EXTERNE",
                check.Bottleneck,
                ctx.Today,
                h,
                check.Charge,
                check.Capacity,
                check.Shortage,
                optionsTried,
                [],
                check.Reason),
            chronology,
            optionsTried,
            h,
            null);
    }

    private static (double? density, string status) Density(double? margin, double? hre, string hreStatus)
    {
        if (hre is null or 0 || hreStatus is "TO_CONFIRM" or "INVALID")
        {
            return (null, "NON_CALCULABLE");
        }

        if (margin is null)
        {
            return (null, "NON_CALCULABLE");
        }

        return (margin.Value / hre.Value, "OK");
    }

    private static int FloatDays(
        DateOnly dateMin,
        DateOnly dateMax,
        DateOnly? customerMax,
        ApsFloatThresholds thr,
        out string status)
    {
        var max = customerMax ?? dateMax;
        var days = Math.Max(0, max.DayNumber - dateMin.DayNumber);
        status = ClassifyFloat(days, thr);
        return days;
    }

    public static string ClassifyFloat(int days, ApsFloatThresholds thr)
        => days switch
        {
            0 => ApsFloatStatuses.Rigide,
            _ when days <= thr.FaibleMaxDays => ApsFloatStatuses.Faible,
            _ when days <= thr.MoyenMaxDays => ApsFloatStatuses.Moyen,
            _ => ApsFloatStatuses.Eleve
        };
}

namespace Axioplan.GammesNomenclatures.Domain.Aps.Flux;

public static class ApsResourceAlgebras
{
    public const string Machine = "MACHINE";
    public const string Labour = "MAIN_OEUVRE";
    public const string Lot = "LOT";
    public const string External = "EXTERNE";
}

public static class ApsSaturationStatuses
{
    public const string SousCharge = "SOUS_CHARGE";
    public const string ProcheLimite = "PROCHE_LIMITE";
    public const string Saturee = "SATUREE";
    public const string Depassement = "DEPASSEMENT";
    public const string NonCalculable = "NON_CALCULABLE";
    public const string SaturationInfinie = "SATURATION_INFINIE";
}

public static class ApsBufferZones
{
    public const string Verte = "VERTE";
    public const string Jaune = "JAUNE";
    public const string Rouge = "ROUGE";
    public const string Noire = "NOIRE";
}

/// <summary>Seuils UI configurables (pas hardcodes hors CDC).</summary>
public sealed record ApsSaturationThresholds(
    double ProcheLimiteRatioOfTarget = 0.90,
    double SatureeRatioOfTarget = 1.00,
    double DepassementRatioOfTarget = 1.05);

public sealed record ApsCompiledLoadLine(
    string ResourceCode,
    string CapacityType,
    double UnitTime,
    double SetupProvision,
    string Unit,
    bool ArtifactValid,
    string ArtifactHash);

public sealed record ApsLaunchQuantity(
    string ArticleCode,
    string ResourceCode,
    DateOnly BucketDate,
    double QtyToLaunch,
    string? FamilyCode = null,
    string? OrderId = null,
    /// <summary>
    /// Article racine de compilation CHARGES (ex. DEMO_PF).
    /// Distinct de <see cref="ArticleCode"/> quand le lancement porte un composant (ex. FIL-DEMO).
    /// </summary>
    string? RootArticleCode = null);

public sealed record ApsLoadBucketResult(
    string ResourceCode,
    string CapacityType,
    string Unit,
    DateOnly BucketDate,
    double QtyToLaunch,
    double UnitTime,
    double SetupProvision,
    double Charge,
    IReadOnlyList<string> Traces,
    IReadOnlyList<ApsExternalMilestone>? ExternalMilestones = null);

public sealed record ApsExternalMilestone(
    string MilestoneType, // REMISE | RECEPTION
    DateOnly Date,
    string ArticleCode,
    double Quantity);

public sealed record ApsLoadWindowResult(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ApsLoadBucketResult> BucketLoads,
    double ChargeCumulee,
    bool Success,
    string? Error);

/// <summary>
/// Charge(ressource, bucket) = Σ qté_lancer × temps unitaire CHARGES + setup.
/// </summary>
public static class LoadEngine
{
    public static ApsLoadBucketResult ComputeBucket(
        ApsCompiledLoadLine loadLine,
        ApsLaunchQuantity launch,
        string expectedResourceType,
        string expectedUnit,
        double? bathMaxCapacity = null)
    {
        if (!loadLine.ArtifactValid)
        {
            throw new InvalidOperationException(
                "CompileInvalideRefuse : artefact CHARGES INVALID — calcul de charge refuse.");
        }

        if (!loadLine.ResourceCode.Equals(launch.ResourceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ressource lancement ≠ ressource artefact CHARGES.");
        }

        var type = expectedResourceType.ToUpperInvariant();
        if (!loadLine.CapacityType.Equals(type, StringComparison.OrdinalIgnoreCase)
            && !(type == ApsResourceAlgebras.External))
        {
            // EXTERNE peut etre marque EXTERNE dans l'artefact
        }

        if (type == ApsResourceAlgebras.External
            || loadLine.CapacityType.Equals(ApsResourceAlgebras.External, StringComparison.OrdinalIgnoreCase))
        {
            var milestones = new[]
            {
                new ApsExternalMilestone("REMISE", launch.BucketDate, launch.ArticleCode, launch.QtyToLaunch),
                new ApsExternalMilestone("RECEPTION", launch.BucketDate.AddDays(7), launch.ArticleCode, launch.QtyToLaunch) // +7 TO_CONFIRM
            };
            return new ApsLoadBucketResult(
                loadLine.ResourceCode,
                ApsResourceAlgebras.External,
                "NONE",
                launch.BucketDate,
                launch.QtyToLaunch,
                0,
                0,
                0,
                [
                    "EXTERNE : aucune charge ressource interne.",
                    "Jalons REMISE et RECEPTION produits uniquement."
                ],
                milestones);
        }

        if (!loadLine.Unit.Equals(expectedUnit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Melange d'unites interdit : artefact={loadLine.Unit}, ressource={expectedUnit}.");
        }

        var traces = new List<string>();
        double charge;

        if (type == ApsResourceAlgebras.Lot)
        {
            if (expectedUnit is "HEURES_MACHINE" or "HEURES_PERSONNE")
            {
                throw new InvalidOperationException("LOT : ne jamais traiter en heures.");
            }

            var maxFill = bathMaxCapacity is > 0 ? bathMaxCapacity.Value : throw new InvalidOperationException(
                "LOT : capacite max par bain requise (TO_CONFIRM si absente).");
            var baths = launch.QtyToLaunch <= 0 ? 0 : Math.Ceiling(launch.QtyToLaunch / maxFill);
            // Bain partiellement rempli = 1 bain entier
            charge = baths + loadLine.SetupProvision;
            traces.Add($"LOT : ceil({launch.QtyToLaunch:F4}/{maxFill:F4}) = {baths} bains (+ setup {loadLine.SetupProvision:F4})");
            traces.Add("Un bain partiellement rempli consomme un bain entier.");
        }
        else
        {
            charge = launch.QtyToLaunch * loadLine.UnitTime + loadLine.SetupProvision;
            traces.Add($"Charge = {launch.QtyToLaunch:F4} × {loadLine.UnitTime:F6} + setup {loadLine.SetupProvision:F4} = {charge:F4} {expectedUnit}");
            traces.Add(type == ApsResourceAlgebras.Machine
                ? "Algebre MACHINE : heures-machine."
                : "Algebre MAIN_OEUVRE : heures-personne.");
        }

        traces.Add($"Artefact CHARGES hash={loadLine.ArtifactHash}");

        return new ApsLoadBucketResult(
            loadLine.ResourceCode,
            type,
            expectedUnit,
            launch.BucketDate,
            launch.QtyToLaunch,
            loadLine.UnitTime,
            loadLine.SetupProvision,
            charge,
            traces);
    }

    public static ApsLoadWindowResult ComputeWindow(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<ApsLoadBucketResult> buckets)
    {
        var inWindow = buckets.Where(b => b.BucketDate >= from && b.BucketDate <= to).ToList();
        return new ApsLoadWindowResult(
            from,
            to,
            inWindow,
            inWindow.Sum(b => b.Charge),
            true,
            null);
    }
}

public sealed record ApsSaturationResult(
    string ResourceCode,
    DateOnly From,
    DateOnly To,
    double ChargeCumulee,
    double CapCum,
    double? Rho,
    double RhoTarget,
    double? RemainingMargin,
    double? EnvelopeGap,
    string Status,
    string Formula,
    string Cause);

public static class SaturationEngine
{
    public static ApsSaturationResult Compute(
        string resourceCode,
        DateOnly from,
        DateOnly to,
        double chargeCumulee,
        double capCum,
        double rhoTarget,
        ApsSaturationThresholds? thresholds = null)
    {
        if (from > to)
        {
            throw new ArgumentException("rho n'existe que sur une fenetre [from,to] valide.");
        }

        var t = thresholds ?? new ApsSaturationThresholds();
        var rhoCible = Math.Clamp(rhoTarget, 0, 1);

        if (capCum < 0)
        {
            throw new ArgumentException("Cap_cum ne peut pas etre negative.");
        }

        if (capCum == 0)
        {
            if (chargeCumulee > 0)
            {
                return new ApsSaturationResult(
                    resourceCode, from, to, chargeCumulee, 0, null, rhoCible, null, null,
                    ApsSaturationStatuses.SaturationInfinie,
                    "rho = Charge_cum / Cap_cum — Cap_cum=0 et Charge>0",
                    "SATURATION_INFINIE : division impossible, charge sans capacite.");
            }

            return new ApsSaturationResult(
                resourceCode, from, to, chargeCumulee, 0, null, rhoCible, null, null,
                ApsSaturationStatuses.NonCalculable,
                "rho = Charge_cum / Cap_cum — Cap_cum=0 et Charge=0",
                "NON_CALCULABLE : aucune capacite ni charge sur la fenetre.");
        }

        var rho = chargeCumulee / capCum;
        var margin = capCum - chargeCumulee;
        var envelopeGap = rho - rhoCible;

        string status;
        if (rho > rhoCible * t.DepassementRatioOfTarget)
        {
            status = ApsSaturationStatuses.Depassement;
        }
        else if (rho >= rhoCible * t.SatureeRatioOfTarget)
        {
            status = ApsSaturationStatuses.Saturee;
        }
        else if (rho >= rhoCible * t.ProcheLimiteRatioOfTarget)
        {
            status = ApsSaturationStatuses.ProcheLimite;
        }
        else
        {
            status = ApsSaturationStatuses.SousCharge;
        }

        return new ApsSaturationResult(
            resourceCode,
            from,
            to,
            chargeCumulee,
            capCum,
            rho,
            rhoCible,
            margin,
            envelopeGap,
            status,
            $"rho(fenetre) = Charge_cum({chargeCumulee:F4}) / Cap_cum({capCum:F4})",
            $"rho={rho:F4} vs rho_cible={rhoCible:F4} → {status} (ecart enveloppe={envelopeGap:F4})");
    }
}

public sealed record ApsMaterialConstraint(
    string MaterialCode,
    bool IsBlocking,
    double Shortage,
    string Cause);

public sealed record ApsBottleneckResult(
    string ConstrainingCode,
    string Kind, // RESOURCE | MATERIAL
    double? Rho,
    DateOnly From,
    DateOnly To,
    string Cause,
    double Charge,
    double Capacity,
    double? MaterialShortage,
    IReadOnlyList<string> AvailableOptions,
    string Explanation);

public static class BottleneckEngine
{
    public static ApsBottleneckResult Identify(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<ApsSaturationResult> saturations,
        ApsMaterialConstraint? yarnConstraint = null)
    {
        if (yarnConstraint is { IsBlocking: true })
        {
            return new ApsBottleneckResult(
                yarnConstraint.MaterialCode,
                "MATERIAL",
                null,
                from,
                to,
                yarnConstraint.Cause,
                0,
                0,
                yarnConstraint.Shortage,
                ["Engagement fil", "Changer bain", "Reporter demande"],
                $"Goulot = FIL : matiere bloque avant les ressources ({yarnConstraint.Cause}). Manque={yarnConstraint.Shortage:F4}.");
        }

        var candidates = saturations
            .Where(s => s.Rho.HasValue)
            .OrderByDescending(s => s.Rho!.Value)
            .ThenBy(s => s.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            var first = saturations.FirstOrDefault();
            return new ApsBottleneckResult(
                first?.ResourceCode ?? "NONE",
                "RESOURCE",
                null,
                from,
                to,
                first?.Status ?? ApsSaturationStatuses.NonCalculable,
                first?.ChargeCumulee ?? 0,
                first?.CapCum ?? 0,
                null,
                [],
                "Aucun rho calculable — goulot non determine (NON_CALCULABLE).");
        }

        var best = candidates[0];
        return new ApsBottleneckResult(
            best.ResourceCode,
            "RESOURCE",
            best.Rho,
            from,
            to,
            best.Status,
            best.ChargeCumulee,
            best.CapCum,
            null,
            ["HS", "Sous-traitance", "Reporter"],
            $"Goulot(fenetre) = argmax rho = {best.ResourceCode} (rho={best.Rho:F4}). " +
            $"Charge={best.ChargeCumulee:F4}, Cap={best.CapCum:F4}.");
    }

    /// <summary>
    /// True uniquement si goulot reellement different, fenetres comparables, et precedent existe.
    /// </summary>
    public static bool ShouldJournalBottleneckMoved(
        ApsBottleneckResult? previous,
        ApsBottleneckResult current,
        bool windowsComparable)
    {
        if (previous is null || !windowsComparable)
        {
            return false;
        }

        return !previous.ConstrainingCode.Equals(current.ConstrainingCode, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(previous.Kind, current.Kind, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ApsBufferStockLine(
    string ArticleCode,
    string? FamilyCode,
    string? ModelCode,
    string? OrderId,
    double Quantity,
    double HrePerUnit,
    string ZoneRole);

public sealed record ApsExpectedMixLine(
    string ArticleCode,
    double ShareOfMix);

public sealed record ApsBufferSnapshot(
    string ChargeCenterCode,
    double BufferHre,
    double DailyEngageableCapacity,
    double? CoverageDays,
    double? Adequacy,
    string AdequacyStatus,
    IReadOnlyList<ApsBufferStockLine> Composition,
    IReadOnlyList<string> Traces);

public sealed record ApsBufferTargetResult(
    double? BufferTargetHre,
    double BufferActualHre,
    double? Gap,
    string Zone,
    double ConsumedRatio,
    string Status,
    string Formula,
    string Cause);

public static class BufferEngine
{
    public static ApsBufferSnapshot ComputeUpstreamBuffer(
        string chargeCenterCode,
        IReadOnlyList<ApsBufferStockLine> upstreamStocks,
        double dailyEngageableCapacityRemail,
        IReadOnlyList<ApsExpectedMixLine>? expectedMix)
    {
        var amont = upstreamStocks
            .Where(s => s.ZoneRole.Equals("AMONT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var bufferHre = amont.Sum(s => s.Quantity * s.HrePerUnit);
        var traces = new List<string>
        {
            $"Buffer_goulot(HRE) = Σ qte × HRE_precompile sur zones AMONT servant {chargeCenterCode}",
            $"Buffer actuel = {bufferHre:F4} HRE ({amont.Count} lignes)"
        };

        double? coverage = null;
        if (dailyEngageableCapacityRemail > 0)
        {
            coverage = bufferHre / dailyEngageableCapacityRemail;
            traces.Add($"Couverture = Buffer_HRE / Cap_engageable_jour = {bufferHre:F4}/{dailyEngageableCapacityRemail:F4} = {coverage:F4} j");
        }
        else
        {
            traces.Add("Couverture NON_CALCULABLE : Cap_engageable journaliere = 0.");
        }

        double? adequacy = null;
        var adequacyStatus = "NON_CALCULABLE";
        if (expectedMix is null || expectedMix.Count == 0)
        {
            adequacyStatus = "TO_CONFIRM";
            traces.Add("Adequation non calculee : aucun mix attendu (contrats/attendus) — TO_CONFIRM.");
        }
        else if (bufferHre <= 0)
        {
            adequacy = 0;
            adequacyStatus = "OK";
            traces.Add("Adequation = 0 (buffer vide).");
        }
        else
        {
            var byArticle = amont
                .GroupBy(s => s.ArticleCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity * x.HrePerUnit), StringComparer.OrdinalIgnoreCase);
            double matched = 0;
            foreach (var exp in expectedMix)
            {
                var expectedHre = bufferHre * Math.Clamp(exp.ShareOfMix, 0, 1);
                byArticle.TryGetValue(exp.ArticleCode, out var actual);
                matched += Math.Min(actual, expectedHre);
            }

            adequacy = matched / bufferHre;
            adequacyStatus = "OK";
            traces.Add($"Adequation = part buffer vs mix attendu = {adequacy:F4}");
        }

        return new ApsBufferSnapshot(
            chargeCenterCode,
            bufferHre,
            dailyEngageableCapacityRemail,
            coverage,
            adequacy,
            adequacyStatus,
            amont,
            traces);
    }

    public static ApsBufferTargetResult ComputeTarget(
        double bufferActualHre,
        double? k,
        IReadOnlyList<double>? upstreamVariances,
        double? reactionLeadDays)
    {
        if (k is null || upstreamVariances is null || upstreamVariances.Count == 0 || reactionLeadDays is null)
        {
            var consumedUnknown = bufferActualHre > 0 ? 0 : 0;
            return new ApsBufferTargetResult(
                null,
                bufferActualHre,
                null,
                ApsBufferZones.Verte,
                consumedUnknown,
                "TO_CONFIRM",
                "Buffer_cible = k × √(Σ variances_amont) + delai_reaction",
                "Donnees k/sigma/delai absentes — cible TO_CONFIRM (pas de sigma invente).");
        }

        var sumVar = upstreamVariances.Sum(v => Math.Max(0, v));
        var target = k.Value * Math.Sqrt(sumVar) + reactionLeadDays.Value;
        var gap = bufferActualHre - target;
        var consumed = target <= 0 ? (bufferActualHre > 0 ? 1.0 : 0) : Math.Max(0, 1.0 - (bufferActualHre / target));
        // Zone = consommation du buffer cible (ecart → jauge → action)
        // Consumed ratio: if actual < target, consumed of "protection" ; CDC: zone on % consumed of buffer
        // Interprétation: ratio consommé = max(0, (cible - actuel) / cible) si actuel < cible,
        // ou 1+ si actuel epuise ; for gauge use how much of target is "used up" from peak view:
        // consumedRatio = 1 - actual/target when actual < target (buffer remaining), else if actual>=target zone VERTE
        // CDC: VERTE <33% consommé, JAUNE 33-67, ROUGE >67, NOIRE 100%+
        // "consommé" = (Buffer_cible - Buffer_actuel) / Buffer_cible when actuel < cible
        double consumedRatio;
        if (target <= 0)
        {
            consumedRatio = 1;
        }
        else if (bufferActualHre >= target)
        {
            consumedRatio = 0;
        }
        else
        {
            consumedRatio = (target - bufferActualHre) / target;
        }

        var zone = consumedRatio switch
        {
            >= 1.0 => ApsBufferZones.Noire,
            > 0.67 => ApsBufferZones.Rouge,
            >= 0.33 => ApsBufferZones.Jaune,
            _ => ApsBufferZones.Verte
        };

        return new ApsBufferTargetResult(
            target,
            bufferActualHre,
            gap,
            zone,
            consumedRatio,
            "OK",
            $"Buffer_cible = {k:F4} × √({sumVar:F4}) + {reactionLeadDays:F4} = {target:F4} HRE",
            $"Ecart={gap:F4} → jauge zone {zone} (consomme={consumedRatio:P0}) — jamais action directe.");
    }
}

public sealed record ApsRopeRecommendation(
    double RecommendedQty,
    IReadOnlyDictionary<string, double> RecommendedMix,
    string Justification,
    string Priority,
    bool UpstreamSpaceAlert,
    bool CreatesWorkOrder,
    IReadOnlyList<string> Traces);

public static class RopeEngine
{
    public static ApsRopeRecommendation RecommendKnittingLaunch(
        double plannedRemailConsumption,
        double bufferTargetHre,
        double bufferActualHre,
        IReadOnlyDictionary<string, double> mixShares,
        bool upstreamSpaceSaturated)
    {
        var deltaBuffer = bufferTargetHre - bufferActualHre;
        var qty = Math.Max(0, plannedRemailConsumption + deltaBuffer);
        var traces = new List<string>
        {
            "Lancement_tricotage = Consommation_remaillage_prevue + (Buffer_cible − Buffer_actuel)",
            $"= {plannedRemailConsumption:F4} + ({bufferTargetHre:F4} − {bufferActualHre:F4}) = {qty:F4}",
            "Recommandation uniquement — aucun OF cree, plan non modifie, pas de sequencement."
        };

        var priority = upstreamSpaceSaturated
            ? "HAUTE_ALERTE_ESPACE"
            : qty > plannedRemailConsumption * 1.2
                ? "HAUTE"
                : qty > 0 ? "NORMALE" : "BASSE";

        if (upstreamSpaceSaturated)
        {
            traces.Add("ALERTE : zone AMONT saturee en espace — limiter ou arreter tricotage (rho_espace).");
        }

        return new ApsRopeRecommendation(
            qty,
            mixShares,
            $"ROPE tricotage : reconstruire buffer vers cible (delta={deltaBuffer:F4} HRE).",
            priority,
            upstreamSpaceSaturated,
            CreatesWorkOrder: false,
            traces);
    }
}

public sealed record ApsSpaceSaturationResult(
    string PlaceCode,
    double OccupiedVolume,
    double MaxVolume,
    double? RhoEspace,
    string Status,
    string Trace,
    bool LimitReached);

public static class SpaceSaturationEngine
{
    public static ApsSpaceSaturationResult Compute(string placeCode, double occupied, double maxVolume)
    {
        if (maxVolume <= 0)
        {
            return new ApsSpaceSaturationResult(
                placeCode, occupied, maxVolume, null,
                ApsSaturationStatuses.NonCalculable,
                "rho_espace NON_CALCULABLE (volume max=0) — independant de rho_capacite.",
                false);
        }

        var rho = occupied / maxVolume;
        var limit = rho >= 1.0;
        return new ApsSpaceSaturationResult(
            placeCode,
            occupied,
            maxVolume,
            rho,
            limit ? ApsSaturationStatuses.Saturee : rho >= 0.85 ? ApsSaturationStatuses.ProcheLimite : ApsSaturationStatuses.SousCharge,
            $"rho_espace({placeCode}) = {occupied:F4}/{maxVolume:F4} = {rho:F4} (≠ rho_capacite)",
            limit);
    }
}

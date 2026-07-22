namespace Axioplan.GammesNomenclatures.Domain.Aps.Capacity;

public static class ApsCapacityUnits
{
    public const string MachineHours = "HEURES_MACHINE";
    public const string LabourHours = "HEURES_PERSONNE";
    public const string Baths = "BAINS";
    public const string PiecesPerDay = "PCS_JOUR"; // lecture demo CDC — conversion HRE = TO_CONFIRM
}

public sealed record ApsCapacityBucket(
    DateOnly BucketDate,
    string? TeamCode,
    double DurationHours,
    bool IsOpenByCalendar);

public sealed record ApsUnavailability(
    DateOnly FromDate,
    DateOnly ToDate,
    double HoursOrBaths,
    string Reason);

public sealed record ApsEtaFactor(
    string ResourceCode,
    string? FamilyCode,
    string? TeamCode,
    double Eta);

public sealed record ApsCapacityReservation(
    string ResourceCode,
    DateOnly BucketDate,
    double Quantity,
    string Unit,
    string AggregateId,
    string? Reason);

public sealed record ApsElasticityTier(
    string Code,
    string ResourceCode,
    double ActivationLeadDays,
    double Gain,
    double TierEta,
    double? CostStoredForFuture,
    string Unit,
    string ConfirmationStatus);

public sealed record ApsExternalEngagementCapacity(
    string PartnerCode,
    string SegmentId,
    double ResidualVolume,
    DateOnly HandoverDate,
    DateOnly ReturnDate,
    DateOnly EngagementDeadline,
    string Status);

public sealed record ApsCapacityStageTrace(
    string Stage,
    double Value,
    string Unit,
    string Formula,
    string Cause);

public sealed record ApsNetCapacityResult(
    string ResourceCode,
    string ResourceType,
    string Unit,
    DateOnly BucketDate,
    string? TeamCode,
    double CapBrute,
    double CapOuverte,
    double CapEffective,
    double CapNette,
    double CapEngageable,
    double RhoTarget,
    double EtaApplied,
    double Reserve,
    double AlreadyReserved,
    IReadOnlyList<ApsCapacityStageTrace> Traces,
    string ConfirmationStatus);

public sealed record ApsCapCumResult(
    string ResourceCode,
    string Unit,
    DateOnly From,
    DateOnly To,
    double CapCumEngageable,
    double CapCumWithActiveTiers,
    IReadOnlyList<ApsElasticityTierEvaluation> Tiers,
    IReadOnlyList<ApsCapacityStageTrace> Traces);

public sealed record ApsElasticityTierEvaluation(
    string Code,
    bool Activable,
    DateOnly? AvailabilityDate,
    double ActivationLeadDays,
    double Gain,
    double TierEta,
    double EffectiveGain,
    string Reason);

/// <summary>
/// Cinq etages auditables Cap_brute → Cap_engageable (CDC §7.1).
/// </summary>
public static class NetCapacityEngine
{
    public static ApsNetCapacityResult ComputeBucket(
        string resourceCode,
        string resourceType,
        string unit,
        ApsCapacityBucket bucket,
        double ResourceCount,
        IReadOnlyList<ApsUnavailability> unavailabilities,
        double eta,
        double rhoTarget,
        double alreadyReserved,
        string confirmationStatus = "OK")
    {
        if (resourceType == "LOT" && unit == ApsCapacityUnits.MachineHours)
        {
            throw new InvalidOperationException("Ressource LOT : ne jamais traiter en heures.");
        }

        var traces = new List<ApsCapacityStageTrace>();

        // Cap_brute
        double capBrute;
        if (!bucket.IsOpenByCalendar)
        {
            capBrute = 0;
            traces.Add(new ApsCapacityStageTrace(
                "Cap_brute", 0, unit,
                "calendrier ferme",
                $"Bucket {bucket.BucketDate} ferme — Cap_brute = 0"));
        }
        else if (resourceType == "LOT")
        {
            // LOT : ResourceCount = bains/jour max sur le bucket
            capBrute = ResourceCount;
            traces.Add(new ApsCapacityStageTrace(
                "Cap_brute", capBrute, unit,
                "bains_disponibles_jour",
                $"LOT : {ResourceCount} bains/jour (jamais en heures)"));
        }
        else
        {
            capBrute = ResourceCount * bucket.DurationHours;
            traces.Add(new ApsCapacityStageTrace(
                "Cap_brute", capBrute, unit,
                "n_ressources × duree(bucket) × calendrier",
                $"{ResourceCount} × {bucket.DurationHours:F2} h"));
        }

        var unavail = unavailabilities
            .Where(u => bucket.BucketDate >= u.FromDate && bucket.BucketDate <= u.ToDate)
            .Sum(u => u.HoursOrBaths);
        var capOuverte = Math.Max(0, capBrute - unavail);
        traces.Add(new ApsCapacityStageTrace(
            "Cap_ouverte",
            capOuverte,
            unit,
            "Cap_brute − Indispo_deterministe",
            $"Indispo = {unavail:F2} ({string.Join(", ", unavailabilities.Where(u => bucket.BucketDate >= u.FromDate && bucket.BucketDate <= u.ToDate).Select(u => u.Reason).DefaultIfEmpty("aucune"))})"));

        var etaClamped = Math.Clamp(eta, 0, 1);
        var capEffective = capOuverte * etaClamped;
        traces.Add(new ApsCapacityStageTrace(
            "Cap_effective",
            capEffective,
            unit,
            "Cap_ouverte × η",
            $"η = {etaClamped:F4} (ressource/famille/equipe)"));

        var rho = Math.Clamp(rhoTarget, 0, 1);
        var reserve = (1 - rho) * capEffective;
        var capNette = capEffective - reserve;
        traces.Add(new ApsCapacityStageTrace(
            "Cap_nette",
            capNette,
            unit,
            "Cap_effective − (1−ρ_cible)×Cap_effective",
            $"ρ_cible = {rho:F3} → reserve = {reserve:F2}"));

        var reserved = Math.Max(0, alreadyReserved);
        var capEngageable = Math.Max(0, capNette - reserved);
        traces.Add(new ApsCapacityStageTrace(
            "Cap_engageable",
            capEngageable,
            unit,
            "Cap_nette − Cap_deja_reservee",
            $"deja reserve = {reserved:F2}"));

        return new ApsNetCapacityResult(
            resourceCode,
            resourceType,
            unit,
            bucket.BucketDate,
            bucket.TeamCode,
            capBrute,
            capOuverte,
            capEffective,
            capNette,
            capEngageable,
            rho,
            etaClamped,
            reserve,
            reserved,
            traces,
            confirmationStatus);
    }

    public static ApsCapCumResult ComputeCapCum(
        string resourceCode,
        string unit,
        DateOnly from,
        DateOnly to,
        DateOnly today,
        IReadOnlyList<ApsNetCapacityResult> bucketResults,
        IReadOnlyList<ApsElasticityTier> tiers)
    {
        var horizonDays = Math.Max(0, to.DayNumber - today.DayNumber);
        var baseCum = bucketResults
            .Where(b => b.BucketDate >= from && b.BucketDate <= to)
            .Sum(b => b.CapEngageable);

        var evals = new List<ApsElasticityTierEvaluation>();
        double tierGain = 0;
        foreach (var tier in tiers.Where(t => t.ResourceCode.Equals(resourceCode, StringComparison.OrdinalIgnoreCase)))
        {
            var available = tier.ActivationLeadDays <= horizonDays;
            var availabilityDate = today.AddDays((int)Math.Ceiling(tier.ActivationLeadDays));
            var effective = available ? tier.Gain * Math.Clamp(tier.TierEta, 0, 1) : 0;
            if (available)
            {
                tierGain += effective;
            }

            evals.Add(new ApsElasticityTierEvaluation(
                tier.Code,
                available,
                available ? availabilityDate : null,
                tier.ActivationLeadDays,
                tier.Gain,
                tier.TierEta,
                effective,
                available
                    ? $"Activable (δ={tier.ActivationLeadDays} j ≤ horizon {horizonDays} j)"
                    : $"Non activable avant δ={tier.ActivationLeadDays} j (horizon {horizonDays} j)"));
        }

        var traces = new List<ApsCapacityStageTrace>
        {
            new("Cap_cum_base", baseCum, unit,
                "Σ Cap_engageable(b) sur [from,to]",
                $"{bucketResults.Count} buckets"),
            new("Cap_cum_paliers", tierGain, unit,
                "Σ gain(p)×η_palier(p) si δ_activation ≤ horizon",
                $"{evals.Count(e => e.Activable)} paliers activables"),
            new("Cap_cum", baseCum + tierGain, unit,
                "Cap_cum_base + paliers",
                $"from={from:yyyy-MM-dd} to={to:yyyy-MM-dd}")
        };

        return new ApsCapCumResult(
            resourceCode,
            unit,
            from,
            to,
            baseCum,
            baseCum + tierGain,
            evals,
            traces);
    }

    public static double ComputeExternalAvailability(
        IReadOnlyList<ApsExternalEngagementCapacity> engagements,
        string segmentId,
        DateOnly asOf,
        DateOnly needBy,
        double preparationLeadDays = 0)
    {
        // CDC : pas d'engagement compatible ⇒ 0. Jamais de Cap_engageable externe calculee.
        return engagements
            .Where(e =>
                e.SegmentId.Equals(segmentId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Status, "OPEN", StringComparison.OrdinalIgnoreCase)
                && e.ReturnDate <= needBy
                && e.HandoverDate >= asOf.AddDays((int)Math.Ceiling(preparationLeadDays))
                && e.EngagementDeadline >= asOf
                && e.ResidualVolume > 0)
            .Sum(e => e.ResidualVolume);
    }

    public static string ResolveUnit(string resourceType)
        => resourceType.ToUpperInvariant() switch
        {
            "LOT" => ApsCapacityUnits.Baths,
            "MAIN_OEUVRE" => ApsCapacityUnits.LabourHours,
            _ => ApsCapacityUnits.MachineHours
        };
}

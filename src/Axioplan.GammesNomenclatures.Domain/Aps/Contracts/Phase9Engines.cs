using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Axioplan.GammesNomenclatures.Domain.Aps.Contracts;

public static class ApsFlowContractStatuses
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
    public const string Frozen = "FROZEN";
    public const string Revised = "REVISED";
    public const string Cancelled = "CANCELLED";
}

public static class ApsBarrierZones
{
    public const string Gelee = "GELEE";
    public const string Negociable = "NEGOCIABLE";
    public const string Libre = "LIBRE";
}

public static class ApsNightlyStatuses
{
    public const string Success = "SUCCESS";
    public const string Partial = "PARTIAL";
    public const string Failed = "FAILED";
}

public static class ApsReplayStatuses
{
    public const string NonEvaluable = "NON_EVALUABLE";
    public const string InsufficientData = "INSUFFICIENT_DATA";
    public const string Failed = "FAILED";
    public const string Passed = "PASSED";
}

public static class ApsDeviationDimensions
{
    public const string Date = "DATE";
    public const string Volume = "VOLUME";
    public const string Composition = "COMPOSITION";
    public const string Bath = "BAIN";
    public const string Regime = "REGIME";
    public const string Circuit = "CIRCUIT";
    public const string Engagement = "ENGAGEMENT";
    public const string Unexpected = "NON_ATTENDU";
}

public sealed record ApsCompositionShare(string ArticleOrFamilyCode, double Share);

public sealed record ApsResourceEnvelope(string ResourceCode, double MaxLoad);

public sealed record ApsFlowContractDraft(
    int IsoWeek,
    int Year,
    double EngagedLoad,
    string BottleneckUnit,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    IReadOnlyList<ApsCompositionShare> Composition,
    IReadOnlyList<ApsResourceEnvelope> Envelopes,
    string? ArticleCode,
    string? FamilyCode,
    string? OrderCode,
    string PlanRef,
    string SourceFingerprint);

public sealed record ApsFlowContract(
    string ContractId,
    int Version,
    string Status,
    int IsoWeek,
    int Year,
    double EngagedLoad,
    string BottleneckUnit,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    IReadOnlyList<ApsCompositionShare> Composition,
    IReadOnlyList<ApsResourceEnvelope> Envelopes,
    string? ArticleCode,
    string? FamilyCode,
    string? OrderCode,
    string PlanRef,
    DateTime PublishedAtUtc,
    string SourceHash);

public sealed record ApsBarrierPolicy(
    int FrozenWeeksStart,
    int FrozenWeeksEnd,
    int NegotiableWeeksStart,
    int NegotiableWeeksEnd);

public sealed record ApsInertiaWeights(
    double DateChange,
    double LoadChange,
    double CompositionChange,
    double CircuitChange,
    double SequenceChange,
    double RePromise,
    double ReservationChurn);

public sealed record ApsInertiaAssessment(
    double Cost,
    double ExpectedGain,
    bool ChangeAllowed,
    string Zone,
    string Explanation,
    IReadOnlyList<string> Breakdown);

public sealed record ApsEstimatorState(
    string Code,
    double? Value,
    int ObservationCount,
    string Source,
    DateTime AsOfUtc,
    string Confidence,
    string Status);

public sealed record ApsRecalibrationResult(
    string EstimatorCode,
    double? OldValue,
    double? NewValue,
    int ObservationCount,
    string Source,
    DateTime AsOfUtc,
    string Confidence,
    string Status,
    string Reason,
    bool InvalidatesArtifacts,
    double RelativeChange);

public sealed record ApsExpectationFactMatch(
    string? ExpectedId,
    long? EventId,
    string Dimension,
    double Magnitude,
    double? HreImpacted,
    string GaugeZone,
    DateTime? OccurredAtUtc,
    DateTime DetectedAtUtc);

public sealed record ApsReplayValidationResult(
    double? GlobalScore,
    IReadOnlyDictionary<string, double> DimensionScores,
    IReadOnlyList<string> DominantCauses,
    string Status,
    bool UnlockM2M6);

public sealed record ApsNervousnessMetrics(
    double PlanDelta,
    double RePromiseRate,
    double ResequenceRate,
    double CircuitReassignRate,
    double BottleneckMoveFreq,
    double EnvelopeViolationFreq,
    double RegimeArbitrationFreq,
    double CompositeIndex);

public static class FlowContractEngine
{
    public static ApsFlowContract CreateDraft(ApsFlowContractDraft draft, string contractId, DateTime nowUtc)
    {
        ValidatePayload(draft.EngagedLoad, draft.Composition, draft.WindowStart, draft.WindowEnd);
        return new ApsFlowContract(
            contractId, 1, ApsFlowContractStatuses.Draft,
            draft.IsoWeek, draft.Year, draft.EngagedLoad, draft.BottleneckUnit,
            draft.WindowStart, draft.WindowEnd, draft.Composition, draft.Envelopes,
            draft.ArticleCode, draft.FamilyCode, draft.OrderCode, draft.PlanRef,
            nowUtc, HashSources(draft.SourceFingerprint, draft.Composition, draft.EngagedLoad, draft.WindowStart, draft.WindowEnd));
    }

    public static ApsFlowContract Publish(ApsFlowContract contract, DateTime nowUtc)
    {
        if (contract.Status is ApsFlowContractStatuses.Published or ApsFlowContractStatuses.Frozen)
        {
            throw new InvalidOperationException("Contrat publié immuable — aucune republier sur la même version.");
        }

        ValidatePayload(contract.EngagedLoad, contract.Composition, contract.WindowStart, contract.WindowEnd);
        return contract with { Status = ApsFlowContractStatuses.Published, PublishedAtUtc = nowUtc };
    }

    public static ApsFlowContract Revise(
        ApsFlowContract published,
        ApsFlowContractDraft newDraft,
        DateTime nowUtc)
    {
        if (published.Status is not (ApsFlowContractStatuses.Published or ApsFlowContractStatuses.Frozen or ApsFlowContractStatuses.Revised))
        {
            throw new InvalidOperationException("Révision réservée aux contrats publiés/gelés.");
        }

        ValidatePayload(newDraft.EngagedLoad, newDraft.Composition, newDraft.WindowStart, newDraft.WindowEnd);
        return new ApsFlowContract(
            published.ContractId,
            published.Version + 1,
            ApsFlowContractStatuses.Revised,
            newDraft.IsoWeek, newDraft.Year, newDraft.EngagedLoad, newDraft.BottleneckUnit,
            newDraft.WindowStart, newDraft.WindowEnd, newDraft.Composition, newDraft.Envelopes,
            newDraft.ArticleCode, newDraft.FamilyCode, newDraft.OrderCode, newDraft.PlanRef,
            nowUtc,
            HashSources(newDraft.SourceFingerprint, newDraft.Composition, newDraft.EngagedLoad, newDraft.WindowStart, newDraft.WindowEnd));
    }

    public static void AssertImmutable(ApsFlowContract contract)
    {
        if (contract.Status is ApsFlowContractStatuses.Published or ApsFlowContractStatuses.Frozen)
        {
            throw new InvalidOperationException("Contrat publié/gelé immuable — créer une nouvelle version.");
        }
    }

    private static void ValidatePayload(
        double load,
        IReadOnlyList<ApsCompositionShare> composition,
        DateOnly from,
        DateOnly to)
    {
        if (load <= 0)
        {
            throw new InvalidOperationException("Contrat refuse : charge engagée absente.");
        }

        if (composition.Count == 0 || composition.Sum(c => c.Share) <= 0)
        {
            throw new InvalidOperationException("Contrat refuse : composition attendue obligatoire (pas seulement le volume).");
        }

        if (to < from)
        {
            throw new InvalidOperationException("Contrat refuse : fenêtre invalide.");
        }
    }

    private static string HashSources(
        string fingerprint,
        IReadOnlyList<ApsCompositionShare> composition,
        double load,
        DateOnly from,
        DateOnly to)
    {
        var sb = new StringBuilder();
        sb.Append(fingerprint).Append('|').Append(load.ToString("F6", CultureInfo.InvariantCulture))
            .Append('|').Append(from).Append('|').Append(to);
        foreach (var c in composition.OrderBy(x => x.ArticleOrFamilyCode, StringComparer.Ordinal))
        {
            sb.Append('|').Append(c.ArticleOrFamilyCode).Append('=').Append(c.Share.ToString("F6", CultureInfo.InvariantCulture));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }
}

public static class BarrierPolicyEngine
{
    public static ApsBarrierPolicy Default => new(0, 1, 2, 4);

    public static string ResolveZone(DateOnly planWeekMonday, DateOnly asOfMonday, ApsBarrierPolicy policy)
    {
        var deltaWeeks = (planWeekMonday.DayNumber - asOfMonday.DayNumber) / 7;
        if (deltaWeeks < 0)
        {
            deltaWeeks = 0;
        }

        if (deltaWeeks >= policy.FrozenWeeksStart && deltaWeeks <= policy.FrozenWeeksEnd)
        {
            return ApsBarrierZones.Gelee;
        }

        if (deltaWeeks >= policy.NegotiableWeeksStart && deltaWeeks <= policy.NegotiableWeeksEnd)
        {
            return ApsBarrierZones.Negociable;
        }

        return ApsBarrierZones.Libre;
    }

    public static void AssertModificationAllowed(string zone, ApsInertiaAssessment? inertia = null)
    {
        if (zone == ApsBarrierZones.Gelee)
        {
            throw new InvalidOperationException("Zone GELEE : aucune modification du contrat engagé / replanification auto interdite.");
        }

        if (zone == ApsBarrierZones.Negociable)
        {
            if (inertia is null)
            {
                throw new InvalidOperationException("Zone NEGOCIABLE : évaluation coût d'inertie obligatoire.");
            }

            if (!inertia.ChangeAllowed)
            {
                throw new InvalidOperationException($"Zone NEGOCIABLE : gain {inertia.ExpectedGain:F2} ≤ coût inertie {inertia.Cost:F2} — contrat conservé.");
            }
        }
    }
}

public static class InertiaCostEngine
{
    public static ApsInertiaAssessment Assess(
        string zone,
        ApsInertiaWeights weights,
        double dateDeltaDays,
        double loadDeltaAbs,
        double compositionDeltaAbs,
        bool circuitChanged,
        bool sequenceChanged,
        bool rePromise,
        bool reservationChurn,
        double expectedGain,
        string? justification)
    {
        var breakdown = new List<string>();
        double cost = 0;
        void Add(string label, double c)
        {
            if (c <= 0) return;
            cost += c;
            breakdown.Add($"{label}={c:F2}");
        }

        Add("date", Math.Abs(dateDeltaDays) * weights.DateChange);
        Add("charge", loadDeltaAbs * weights.LoadChange);
        Add("composition", compositionDeltaAbs * weights.CompositionChange);
        if (circuitChanged) Add("circuit", weights.CircuitChange);
        if (sequenceChanged) Add("sequence", weights.SequenceChange);
        if (rePromise) Add("re-promesse", weights.RePromise);
        if (reservationChurn) Add("reservation", weights.ReservationChurn);

        var allowed = zone switch
        {
            ApsBarrierZones.Gelee => false,
            ApsBarrierZones.Libre => true,
            ApsBarrierZones.Negociable => expectedGain > cost && !string.IsNullOrWhiteSpace(justification),
            _ => false
        };

        var explanation = zone == ApsBarrierZones.Negociable
            ? (allowed
                ? $"Changement autorisé : gain {expectedGain:F2} > inertie {cost:F2}. Justif: {justification}"
                : $"Changement refusé : gain {expectedGain:F2} ≤ inertie {cost:F2} ou justification absente.")
            : zone == ApsBarrierZones.Libre
                ? "Zone LIBRE : régénération complète autorisée."
                : "Zone GELEE : modification interdite.";

        return new ApsInertiaAssessment(cost, expectedGain, allowed, zone, explanation, breakdown);
    }
}

public static class RecalibrationEngine
{
    public static ApsRecalibrationResult ApplyEwma(
        string code,
        double? oldValue,
        double observation,
        int observationCount,
        double alpha,
        double invalidationThreshold,
        string source,
        DateTime asOfUtc)
    {
        var previous = oldValue ?? observation;
        var next = alpha * observation + (1 - alpha) * previous;
        var relative = previous == 0 ? Math.Abs(next) : Math.Abs(next - previous) / Math.Abs(previous);
        var invalidates = relative > invalidationThreshold;
        return new ApsRecalibrationResult(
            code,
            oldValue,
            next,
            observationCount + 1,
            source,
            asOfUtc,
            observationCount + 1 >= 5 ? "MOYENNE" : "TO_CONFIRM",
            invalidates ? "INVALIDATING" : "STABLE",
            invalidates
                ? $"Variation relative {relative:P1} > seuil {invalidationThreshold:P1}"
                : $"Variation relative {relative:P1} ≤ seuil — pas d'invalidation",
            invalidates,
            relative);
    }
}

public static class ExpectationFactMatcher
{
    public static IReadOnlyList<ApsExpectationFactMatch> Match(
        IReadOnlyList<(string ExpectedId, string Dimension, DateTime Earliest, DateTime Latest, double? ExpectedVolume, string? CompositionKey)> expectations,
        IReadOnlyList<(long EventId, string EventType, DateTime OccurredAt, double? Volume, string? CompositionKey)> facts,
        DateTime detectedAtUtc)
    {
        var matches = new List<ApsExpectationFactMatch>();
        var usedFacts = new HashSet<long>();

        foreach (var exp in expectations.OrderBy(e => e.Earliest))
        {
            var candidates = facts
                .Where(f => !usedFacts.Contains(f.EventId)
                            && f.OccurredAt >= exp.Earliest.AddDays(-2)
                            && f.OccurredAt <= exp.Latest.AddDays(2))
                .OrderBy(f => Math.Abs((f.OccurredAt - exp.Latest).TotalHours))
                .ToList();

            if (candidates.Count == 0)
            {
                if (detectedAtUtc > exp.Latest)
                {
                    matches.Add(new ApsExpectationFactMatch(
                        exp.ExpectedId, null, ApsDeviationDimensions.Date, 1,
                        null, "ROUGE", null, detectedAtUtc));
                    if (exp.ExpectedVolume is not null)
                    {
                        matches.Add(new ApsExpectationFactMatch(
                            exp.ExpectedId, null, ApsDeviationDimensions.Volume, exp.ExpectedVolume.Value,
                            null, "ROUGE", null, detectedAtUtc));
                    }
                }

                continue;
            }

            var fact = candidates[0];
            usedFacts.Add(fact.EventId);

            var dateMag = Math.Abs((fact.OccurredAt.Date - exp.Latest.Date).TotalDays);
            if (dateMag > 0)
            {
                matches.Add(new ApsExpectationFactMatch(
                    exp.ExpectedId, fact.EventId, ApsDeviationDimensions.Date, dateMag,
                    null, Gauge(dateMag), fact.OccurredAt, detectedAtUtc));
            }

            if (exp.CompositionKey is not null && fact.CompositionKey is not null
                && !exp.CompositionKey.Equals(fact.CompositionKey, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(new ApsExpectationFactMatch(
                    exp.ExpectedId, fact.EventId, ApsDeviationDimensions.Composition, 1,
                    null, "JAUNE", fact.OccurredAt, detectedAtUtc));
            }
        }

        foreach (var fact in facts.Where(f => !usedFacts.Contains(f.EventId)))
        {
            matches.Add(new ApsExpectationFactMatch(
                null, fact.EventId, ApsDeviationDimensions.Unexpected, 1,
                null, "JAUNE", fact.OccurredAt, detectedAtUtc));
        }

        return matches;
    }

    private static string Gauge(double days)
        => days switch
        {
            <= 1 => "VERTE",
            <= 3 => "JAUNE",
            _ => "ROUGE"
        };
}

public static class ReplayValidationEngine
{
    public static ApsReplayValidationResult Evaluate(
        IReadOnlyList<ApsExpectationFactMatch>? matches,
        bool hasPriorPublishedPlan)
    {
        if (!hasPriorPublishedPlan)
        {
            return new ApsReplayValidationResult(null, new Dictionary<string, double>(),
                ["Aucun plan publié semaine précédente"], ApsReplayStatuses.NonEvaluable, false);
        }

        if (matches is null || matches.Count == 0)
        {
            return new ApsReplayValidationResult(null, new Dictionary<string, double>(),
                ["Données insuffisantes"], ApsReplayStatuses.InsufficientData, false);
        }

        var dims = new[]
        {
            ApsDeviationDimensions.Date, ApsDeviationDimensions.Volume, ApsDeviationDimensions.Composition,
            ApsDeviationDimensions.Bath, ApsDeviationDimensions.Circuit, ApsDeviationDimensions.Engagement,
            ApsDeviationDimensions.Unexpected
        };

        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dims)
        {
            var subset = matches.Where(m => m.Dimension == d).ToList();
            scores[d] = subset.Count == 0 ? 1.0 : Math.Max(0, 1.0 - subset.Average(m => Math.Min(1, m.Magnitude / 5.0)));
        }

        var global = scores.Values.Average();
        var status = global >= 0.85 ? ApsReplayStatuses.Passed : ApsReplayStatuses.Failed;
        var dominant = scores.OrderBy(kv => kv.Value).Take(3).Select(kv => $"{kv.Key}:{kv.Value:F2}").ToList();
        return new ApsReplayValidationResult(global, scores, dominant, status, status == ApsReplayStatuses.Passed);
    }

    public static void AssertM2M6Locked(ApsReplayValidationResult recipe)
    {
        if (!recipe.UnlockM2M6)
        {
            throw new InvalidOperationException(
                $"M2/M6 verrouillés tant que la recette n'est pas PASSED (statut={recipe.Status}).");
        }
    }
}

public static class NervousnessEngine
{
    public static ApsNervousnessMetrics Compute(
        double planDelta,
        double rePromiseRate,
        double resequenceRate,
        double circuitReassignRate,
        int bottleneckMoves,
        int envelopeViolations,
        int regimeArbitrations,
        int daysInPeriod)
    {
        var denom = Math.Max(1, daysInPeriod);
        var metrics = new ApsNervousnessMetrics(
            planDelta,
            rePromiseRate,
            resequenceRate,
            circuitReassignRate,
            bottleneckMoves / (double)denom,
            envelopeViolations / (double)denom,
            regimeArbitrations / (double)denom,
            0);
        var composite = metrics.PlanDelta * 0.25
                        + metrics.RePromiseRate * 0.2
                        + metrics.ResequenceRate * 0.15
                        + metrics.CircuitReassignRate * 0.1
                        + metrics.BottleneckMoveFreq * 0.1
                        + metrics.EnvelopeViolationFreq * 0.1
                        + metrics.RegimeArbitrationFreq * 0.1;
        return metrics with { CompositeIndex = composite };
    }
}

public static class NightlyCycleSteps
{
    public static IReadOnlyList<string> Ordered { get; } =
    [
        "ReplayFaits",
        "ProjeterEtats",
        "RecalculerEstimateurs",
        "EmettreCoefficientRecalibre",
        "InvaliderArtefacts",
        "Recompiler",
        "RelancerCbnCharge",
        "ReplanifierHorsGelee",
        "PublierPlan",
        "CalculerNervosite"
    ];
}

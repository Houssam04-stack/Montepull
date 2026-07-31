using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Capacity;
using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;

namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record ApsResourceCapacitySummaryDto(
    string ResourceCode,
    string Label,
    string ResourceType,
    string Unit,
    DateOnly From,
    DateOnly To,
    double CapCumEngageable,
    double CapCumWithTiers,
    IReadOnlyList<ApsNetCapacityResult> Buckets,
    IReadOnlyList<ApsElasticityTierEvaluation> Tiers);

public sealed record ApsPlanningRequest(
    DateOnly From,
    DateOnly To,
    ApsSegmentCbnRunRequest? CbnRequest = null,
    long? SegmentCbnRunId = null,
    bool PublishFlux = false,
    ApsSaturationThresholds? Thresholds = null,
    CalculationSourceType SourceType = CalculationSourceType.Real,
    Guid? Mvp0CampaignId = null,
    bool RunSegmentCascade = true);

public sealed record ApsPlanningResultDto(
    DateOnly From,
    DateOnly To,
    long? SegmentCbnRunId,
    ApsSegmentCbnResult? CbnResult,
    IReadOnlyList<ApsResourceCapacitySummaryDto> Capacities,
    ApsFluxComputeResultDto Flux,
    IReadOnlyList<string> Traces,
    Guid? Mvp0CampaignId = null,
    string? Mvp0GateOutcome = null);

public sealed record ApsSegmentCascadeRunDto(
    IReadOnlyList<ApsSegmentCbnRunDto> SegmentRuns,
    long? LastRunId,
    ApsSegmentCbnResult? LastResult);

public sealed record ApsSegmentCbnRunHistoryDto(
    long Id,
    string DemandId,
    string Segment,
    string FinishedArticleCode,
    bool Success,
    DateTime CreatedAtUtc,
    ApsSegmentCbnResult Result);

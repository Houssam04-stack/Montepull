using Axioplan.GammesNomenclatures.Domain.Aps.Capacity;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;

namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record ApsCapacityResourceDto(
    string Code,
    string Label,
    string ResourceType,
    string Unit,
    double ResourceCount,
    double DefaultDurationHours,
    double RhoTarget,
    string ConfirmationStatus);

public sealed record ApsCapacityWindowRequest(
    string ResourceCode,
    DateOnly From,
    DateOnly To,
    string? FamilyCode = null,
    string? TeamCode = null);

public sealed record ApsCapacityWindowResultDto(
    ApsCapacityResourceDto Resource,
    IReadOnlyList<ApsNetCapacityResult> Buckets,
    ApsCapCumResult CapCum,
    double ExternalAvailability,
    string ExternalNote,
    IReadOnlyList<ApsCapacityReservation> Reservations,
    IReadOnlyList<ApsElasticityTierEvaluation> Tiers);

public sealed record ApsSegmentCbnRunRequest(
    string DemandId,
    string FinishedArticleCode,
    double Quantity,
    DateOnly NeedDate,
    string Segment,
    string CircuitChain,
    string CustomerCompatRule,
    bool IsMtsReplenishment = false,
    string? OrderAggregateId = null,
    double LotMultiple = 1,
    double SafetyStock = 0);

public sealed record ApsSegmentCbnRunDto(
    long? RunId,
    ApsSegmentCbnResult Result,
    string? ArtifactHash,
    string ArtifactStatus);

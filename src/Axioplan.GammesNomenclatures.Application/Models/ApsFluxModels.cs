using Axioplan.GammesNomenclatures.Domain.Aps.Flux;

namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record ApsFluxWindowRequest(
    DateOnly From,
    DateOnly To,
    long? SegmentCbnRunId = null,
    bool Publish = false,
    ApsSaturationThresholds? Thresholds = null);

public sealed record ApsFluxComputeResultDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ApsLoadBucketResult> Loads,
    IReadOnlyList<ApsSaturationResult> Saturations,
    ApsBottleneckResult Bottleneck,
    bool BottleneckMovedJournaled,
    ApsBufferSnapshot Buffer,
    ApsBufferTargetResult BufferTarget,
    ApsRopeRecommendation Rope,
    IReadOnlyList<ApsSpaceSaturationResult> SpaceSaturations,
    long? PersistRunId,
    IReadOnlyList<string> Traces);

public sealed record ApsLoadRunSummaryDto(
    long Id,
    DateOnly From,
    DateOnly To,
    DateTime CreatedAtUtc,
    string? BottleneckCode);

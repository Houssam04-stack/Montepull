using Axioplan.GammesNomenclatures.Domain.Aps.Contracts;

namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record ApsNightlyRunDto(
    long Id,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    string Status,
    string? Notes,
    IReadOnlyList<ApsNightlyStepDto> Steps);

public sealed record ApsNightlyStepDto(
    int StepOrder,
    string StepName,
    string Status,
    string Detail,
    DateTime AtUtc);

public sealed record ApsFlowContractDto(
    string ContractId,
    int Version,
    string Status,
    int IsoWeek,
    int Year,
    double EngagedLoad,
    string BottleneckUnit,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    string CompositionJson,
    string PlanRef,
    string SourceHash,
    DateTime PublishedAtUtc);

public sealed record ApsReplayDto(
    long Id,
    string Status,
    double? GlobalScore,
    bool UnlockM2M6,
    string DetailJson,
    DateTime CreatedAtUtc);

public sealed record ApsNervousnessDto(
    long Id,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    double CompositeIndex,
    string MetricsJson,
    DateTime CreatedAtUtc);

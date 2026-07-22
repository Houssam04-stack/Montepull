using Axioplan.GammesNomenclatures.Domain.Aps.Ctp;

namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record ApsCtpEvaluateRequest(
    string ArticleCode,
    string CustomerCode,
    double Quantity,
    DateOnly DesiredDate,
    DateOnly? CustomerMaxDate = null,
    double? Margin = null,
    int Priority = 5,
    string? DeclaredRegime = null,
    double? QuantityTolerance = null,
    string? OrderReference = null,
    string? ArticleCategory = null,
    string? CustomerCategory = null,
    string? Scenario = null);

public sealed record ApsCtpPromiseDto(
    long Id,
    string PromiseId,
    string DemandId,
    string Status,
    string Outcome,
    DateOnly? ProposedDate,
    string EffectiveRegime,
    string Bottleneck,
    double? Reliability,
    string ReliabilityStatus,
    double? Hre,
    double? MarginDensity,
    int? FloatDays,
    string FloatStatus,
    string? CircuitCode,
    string? BathCode,
    string? ExternalEngagement,
    DateTime CreatedAtUtc,
    string PayloadJson);

public sealed record ApsCtpEvaluateResultDto(
    ApsCtpAnswer Answer,
    long ElapsedMs,
    bool IsEvaluationOnly);

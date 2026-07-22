using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.Expectations;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;

namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record ApsJournalQuery(
    string? EventType = null,
    string? AggregateId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Take = 200);

public sealed record ApsExpectationQuery(
    string? ExpectedType = null,
    string? AggregateId = null,
    string? Grain = null,
    string? EmittedBy = null,
    int Take = 200);

public sealed record ApsPlaceDto(
    int Id,
    int? ParentId,
    string Level,
    string Code,
    string Label,
    int? CalendarId);

public sealed record ApsStockZoneDto(
    int Id,
    int PlaceId,
    string PlaceCode,
    string Role,
    int? ServedChargeCenterId);

public sealed record ApsChargeCenterDto(
    int Id,
    int PlaceId,
    string Code,
    string Label,
    string ResourceType);

public sealed record ApsResourceScheduleDto(
    string ResourceCode,
    string? CalendarCode,
    string RegimeCode,
    string? DefaultTeamCode,
    double TeamDurationHours);

public sealed record ApsChargePostDto(
    int Id,
    int ChargeCenterId,
    string Code,
    string Label);

public sealed record ApsCalendarDto(int Id, string Code, string Label);
public sealed record ApsWorkRegimeDto(int Id, string Code, string Label);
public sealed record ApsTeamDto(int Id, int RegimeId, string Code, TimeSpan StartTime, TimeSpan EndTime);

public sealed record ApsStockLotDto(
    int Id,
    int ArticleId,
    string ArticleCode,
    string? LotCode,
    string? BathCode,
    double Quantity,
    string Unit,
    string Status,
    int? StockZoneId);

public sealed record ApsCircuitDto(
    int Id,
    string Code,
    string SegmentId,
    string? OperationCode,
    string CircuitType,
    string? PostCode,
    string? SupplierCode,
    double UnitTimeMinutes,
    double YieldRate,
    double ActivationLeadDays,
    double TraversalLeadDays);

public sealed record ApsExternalEngagementDto(
    int Id,
    string PartnerCode,
    string SegmentId,
    double Quantity,
    DateOnly HandoverDate,
    DateOnly ReturnDate,
    DateOnly EngagementDeadline,
    string Status,
    double ResidualVolume);

public sealed record ApsSupplierLeadTimeDto(
    int Id,
    string ArticleCode,
    string SupplierCode,
    double StandardLeadDays,
    double ObservedMuDays,
    double ObservedSigmaDays,
    double? Moq,
    double? Multiple);

public sealed record ApsCompiledArtifactDto(
    long Id,
    string RootArticleCode,
    string Segment,
    string CircuitChain,
    string ArtifactKind,
    string PayloadJson,
    string SourceHash,
    string Status,
    DateTime CompiledAtUtc,
    string Notes);

public sealed record CompileApsRequest(
    string RootArticleCode,
    string Segment,
    string CircuitChain,
    string BomFingerprint,
    string RoutingFingerprint,
    string CircuitsFingerprint = "",
    string YieldsFingerprint = "",
    string StandardsFingerprint = "",
    string EtaFingerprint = "",
    string UnitsFingerprint = "",
    string DecouplingFingerprint = "",
    IReadOnlyList<CompileBomLineInput>? BomLines = null,
    IReadOnlyList<CompileRoutingOpInput>? RoutingOps = null);

public sealed record CompileBomLineInput(string ComponentCode, double Qty, string? OperationCode);
public sealed record CompileRoutingOpInput(string OperationCode, string ResourceCode, double UnitMinutes, bool IsBottleneck);

// Re-export domain commands for Application consumers
public static class ApsCommandAliases
{
    public static AppendApsJournalFactCommand Fact(
        string eventType,
        DateTime occurredAtUtc,
        string aggregateId,
        string payloadJson,
        string actor,
        string? causationId = null,
        string? correlationId = null)
        => new(eventType, occurredAtUtc, aggregateId, payloadJson, actor, causationId, correlationId);

    public static EmitApsExpectationCommand Expectation(
        string expectedId,
        string expectedType,
        string emittedBy,
        string grain,
        DateTime earliest,
        DateTime latest,
        string aggregateId,
        string payloadJson,
        string? planRef = null,
        double? hre = null,
        string? causationId = null)
        => new(expectedId, expectedType, emittedBy, grain, earliest, latest, aggregateId, payloadJson, planRef, hre, causationId);
}

using Axioplan.GammesNomenclatures.Domain.Mvp0;

namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record Mvp0CampaignDto(
    Guid Id,
    string Code,
    string FamilyCode,
    string SiteCode,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string Owner,
    string Status,
    string Provenance,
    int ImportVersion,
    string? GateOutcome,
    DateTime CreatedAtUtc);

public sealed record Mvp0ImportBatchDto(
    long Id,
    Guid CampaignId,
    string DataType,
    string FileName,
    string FileHash,
    int Version,
    int LinesRead,
    int LinesImported,
    int LinesRejected,
    string Status,
    string Provenance,
    DateTime ImportedAtUtc,
    string ReportJson);

public sealed record Mvp0WorkflowStateDto(
    Mvp0CampaignDto Campaign,
    IReadOnlyList<string> CompletedSteps,
    string CurrentStep,
    IReadOnlyList<Mvp0Anomaly> Anomalies,
    IReadOnlyList<Mvp0Bypass> Bypasses,
    Mvp0InputReliabilityResult? InputReliability,
    Mvp0BacktestResult? Backtest,
    Mvp0ResultReliabilityResult? ResultReliability,
    Mvp0GateDecision? Gate,
    string? PlannerName,
    string? PlannerComment,
    bool PlannerApproved,
    string ReportHtml);

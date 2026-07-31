using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Mvp0;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IMvp0Repository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
    Task SeedDemoAsync(CancellationToken cancellationToken = default);

    Task<Mvp0Campaign> CreateCampaignAsync(Mvp0Campaign campaign, CancellationToken cancellationToken = default);
    Task<Mvp0Campaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpdateCampaignStatusAsync(Guid id, string status, string? gateOutcome = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Mvp0CampaignDto>> ListCampaignsAsync(CancellationToken cancellationToken = default);

    Task<Mvp0ImportBatchDto> SaveImportBatchAsync(Mvp0ImportBatchDto batch, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Mvp0ImportBatchDto>> ListImportBatchesAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task ReplaceWorkingSetAsync(
        Guid campaignId,
        IReadOnlyList<Mvp0ArticleRow> articles,
        IReadOnlyList<Mvp0BomRow> boms,
        IReadOnlyList<Mvp0RoutingOpRow> ops,
        IReadOnlyList<Mvp0CalendarRow> calendars,
        IReadOnlyList<Mvp0WorkOrderActual> wos,
        IReadOnlySet<string> centers,
        IReadOnlySet<string> bomOpLinks,
        CancellationToken cancellationToken = default);

    Task SaveImportMappingAsync(Guid campaignId, long batchId, string dataType, string mappingJson, CancellationToken cancellationToken = default);
    Task SaveValidationRulesSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Convertit un fichier Excel en feuilles CSV pour l'assistant MVP-0 (aucune persistance).</summary>
    Task<IReadOnlyList<(string SheetName, string Csv)>> ConvertExcelToCsvSheetsAsync(
        byte[] fileContent,
        string? fileName = null,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Mvp0ArticleRow> Articles, IReadOnlyList<Mvp0BomRow> Boms, IReadOnlyList<Mvp0RoutingOpRow> Ops,
        IReadOnlyList<Mvp0CalendarRow> Calendars, IReadOnlyList<Mvp0WorkOrderActual> Wos, IReadOnlySet<string> Centers,
        IReadOnlySet<string> BomOpLinks)> LoadWorkingSetAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task SaveAnomaliesAsync(Guid campaignId, IReadOnlyList<Mvp0Anomaly> anomalies, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Mvp0Anomaly>> ListAnomaliesAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task SaveBypassAsync(Guid campaignId, Mvp0Bypass bypass, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Mvp0Bypass>> ListBypassesAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task SaveReliabilityAsync(Guid campaignId, Mvp0InputReliabilityResult input, Mvp0ResultReliabilityResult? result, CancellationToken cancellationToken = default);
    Task SaveBacktestAsync(Guid campaignId, Mvp0BacktestResult backtest, CancellationToken cancellationToken = default);
    Task SaveGateAsync(Guid campaignId, Mvp0GateDecision gate, string plannerName, string comment, bool approved, CancellationToken cancellationToken = default);
    Task SaveReportAsync(Guid campaignId, string html, CancellationToken cancellationToken = default);
    Task<string?> GetReportAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task<(string? PlannerName, string? Comment, bool Approved)> GetPlannerReviewAsync(Guid campaignId, CancellationToken cancellationToken = default);
    Task<(Mvp0InputReliabilityResult? Input, Mvp0ResultReliabilityResult? Result, Mvp0BacktestResult? Backtest, Mvp0GateDecision? Gate)> GetScoresAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task<Mvp0ReliabilityWeights> GetWeightsAsync(CancellationToken cancellationToken = default);
    Task<(double Go, double GoRes, double MinCycle, double MaxCycle, int AgingDays, int StaleDays, int DateTol, double DurTol)> GetThresholdsAsync(CancellationToken cancellationToken = default);
}

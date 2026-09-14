using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.Mvp0;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class Mvp0ApsGateGuardTests
{
    [Fact]
    public void IsApproved_accepts_go_outcomes_only()
    {
        var go = new Mvp0CampaignDto(Guid.NewGuid(), "C", "PULL", "S", default, default, "o", "GO", "REAL", 1, Mvp0GateOutcomes.Go, DateTime.UtcNow);
        var res = go with { GateOutcome = Mvp0GateOutcomes.GoWithReservations };
        var no = go with { GateOutcome = Mvp0GateOutcomes.NoGo };

        Assert.True(Mvp0ApsGateGuard.IsApproved(go));
        Assert.True(Mvp0ApsGateGuard.IsApproved(res));
        Assert.False(Mvp0ApsGateGuard.IsApproved(no));
    }

    [Fact]
    public void Demo_source_bypasses_gate_requirement()
    {
        var guard = new Mvp0ApsGateGuard(new FakeMvp0Repo([]));
        var demo = guard.RequireApprovedCampaignAsync(CalculationSourceType.Demo).GetAwaiter().GetResult();
        Assert.Equal("DEMO-BYPASS", demo.Code);
    }

    [Fact]
    public void Real_source_throws_without_go_campaign()
    {
        var guard = new Mvp0ApsGateGuard(new FakeMvp0Repo([
            new Mvp0CampaignDto(Guid.NewGuid(), "X", "PULL", "S", default, default, "o", "DRAFT", "SIMULATED", 0, null, DateTime.UtcNow)
        ]));
        Assert.Throws<InvalidOperationException>(() =>
            guard.RequireApprovedCampaignAsync(CalculationSourceType.Real).GetAwaiter().GetResult());
    }

    private sealed class FakeMvp0Repo(IReadOnlyList<Mvp0CampaignDto> campaigns) : IMvp0Repository
    {
        public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeedDemoAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Mvp0CampaignDto>> ListCampaignsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(campaigns);
        public Task<Mvp0Campaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Mvp0Campaign?>(null);
        public Task<Mvp0Campaign> CreateCampaignAsync(Mvp0Campaign campaign, CancellationToken cancellationToken = default) => Task.FromResult(campaign);
        public Task UpdateCampaignStatusAsync(Guid id, string status, string? gateOutcome = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Mvp0ImportBatchDto> SaveImportBatchAsync(Mvp0ImportBatchDto batch, CancellationToken cancellationToken = default) => Task.FromResult(batch);
        public Task<IReadOnlyList<Mvp0ImportBatchDto>> ListImportBatchesAsync(Guid campaignId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Mvp0ImportBatchDto>>([]);
        public Task ReplaceWorkingSetAsync(Guid campaignId, IReadOnlyList<Mvp0ArticleRow> articles, IReadOnlyList<Mvp0BomRow> boms, IReadOnlyList<Mvp0RoutingOpRow> ops, IReadOnlyList<Mvp0CalendarRow> calendars, IReadOnlyList<Mvp0WorkOrderActual> wos, IReadOnlySet<string> centers, IReadOnlySet<string> bomOpLinks, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveImportMappingAsync(Guid campaignId, long batchId, string dataType, string mappingJson, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveValidationRulesSnapshotAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<(string SheetName, string Csv)>> ConvertExcelToCsvSheetsAsync(byte[] fileContent, string? fileName = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<(string, string)>>([]);
        public Task<(IReadOnlyList<Mvp0ArticleRow> Articles, IReadOnlyList<Mvp0BomRow> Boms, IReadOnlyList<Mvp0RoutingOpRow> Ops, IReadOnlyList<Mvp0CalendarRow> Calendars, IReadOnlyList<Mvp0WorkOrderActual> Wos, IReadOnlySet<string> Centers, IReadOnlySet<string> BomOpLinks)> LoadWorkingSetAsync(Guid campaignId, CancellationToken cancellationToken = default)
            => Task.FromResult((Array.Empty<Mvp0ArticleRow>() as IReadOnlyList<Mvp0ArticleRow>, Array.Empty<Mvp0BomRow>() as IReadOnlyList<Mvp0BomRow>, Array.Empty<Mvp0RoutingOpRow>() as IReadOnlyList<Mvp0RoutingOpRow>, Array.Empty<Mvp0CalendarRow>() as IReadOnlyList<Mvp0CalendarRow>, Array.Empty<Mvp0WorkOrderActual>() as IReadOnlyList<Mvp0WorkOrderActual>, new HashSet<string>() as IReadOnlySet<string>, new HashSet<string>() as IReadOnlySet<string>));
        public Task SaveAnomaliesAsync(Guid campaignId, IReadOnlyList<Mvp0Anomaly> anomalies, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Mvp0Anomaly>> ListAnomaliesAsync(Guid campaignId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Mvp0Anomaly>>([]);
        public Task SaveBypassAsync(Guid campaignId, Mvp0Bypass bypass, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Mvp0Bypass>> ListBypassesAsync(Guid campaignId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Mvp0Bypass>>([]);
        public Task SaveReliabilityAsync(Guid campaignId, Mvp0InputReliabilityResult input, Mvp0ResultReliabilityResult? result, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveBacktestAsync(Guid campaignId, Mvp0BacktestResult backtest, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveGateAsync(Guid campaignId, Mvp0GateDecision gate, string plannerName, string comment, bool approved, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveReportAsync(Guid campaignId, string html, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetReportAsync(Guid campaignId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<(Mvp0InputReliabilityResult? Input, Mvp0ResultReliabilityResult? Result, Mvp0BacktestResult? Backtest, Mvp0GateDecision? Gate)> GetScoresAsync(Guid campaignId, CancellationToken cancellationToken = default)
            => Task.FromResult<(Mvp0InputReliabilityResult?, Mvp0ResultReliabilityResult?, Mvp0BacktestResult?, Mvp0GateDecision?)>((null, null, null, null));
        public Task<(string? PlannerName, string? Comment, bool Approved)> GetPlannerReviewAsync(Guid campaignId, CancellationToken cancellationToken = default)
            => Task.FromResult<(string?, string?, bool)>((null, null, false));
        public Task<Mvp0ReliabilityWeights> GetWeightsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new Mvp0ReliabilityWeights(15, 15, 15, 10, 8, 5, 8, 14, 10, 10, 10, 10, 1));
        public Task<(double Go, double GoRes, double MinCycle, double MaxCycle, int AgingDays, int StaleDays, int DateTol, double DurTol)> GetThresholdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((85.0, 70.0, 0.01, 8.0, 30, 90, 3, 0.25));
    }
}

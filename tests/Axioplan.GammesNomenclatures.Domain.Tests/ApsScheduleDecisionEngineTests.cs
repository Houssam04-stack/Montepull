using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Axioplan.GammesNomenclatures.Domain.Aps.Schedule;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class ApsScheduleDecisionEngineTests
{
    private static readonly DateOnly From = new(2026, 7, 20);
    private static readonly DateOnly To = new(2026, 7, 31);
    private static readonly DateOnly Today = new(2026, 7, 22);
    private static readonly DateOnly Need = new(2026, 8, 10);

    [Fact]
    public void Builds_of_operations_critical_path_and_working_days()
    {
        var loads = new[]
        {
            new ApsLoadBucketResult("CC_TRICOTAGE", ApsResourceAlgebras.Machine, "HEURES_MACHINE", From, 80, 0.1, 0, 8.5, ["t"]),
            new ApsLoadBucketResult("CC_REMAILLAGE", ApsResourceAlgebras.Labour, "HEURES_PERSONNE", From.AddDays(2), 100, 0.2, 0, 20, ["t"]),
            new ApsLoadBucketResult("CC_REMAILLAGE", ApsResourceAlgebras.Labour, "HEURES_PERSONNE", From.AddDays(3), 40, 0.2, 0, 8, ["t"])
        };
        var sats = new[]
        {
            SaturationEngine.Compute("CC_TRICOTAGE", From, To, 8.5, 40, 0.85),
            SaturationEngine.Compute("CC_REMAILLAGE", From, To, 28, 30, 0.85)
        };
        var bn = BottleneckEngine.Identify(From, To, sats);

        var result = ApsScheduleDecisionEngine.Build(
            From, To, loads, sats, bn,
            needDate: Need,
            today: Today,
            demandId: "CMD-DEMO",
            finishedArticleCode: "DEMO_PF");

        Assert.Contains(result.Tasks, t => t.Kind == ApsScheduleTaskKinds.WorkOrder);
        Assert.True(result.Tasks.Count(t => t.Kind == ApsScheduleTaskKinds.Operation) >= 3);
        Assert.Equal("CC_REMAILLAGE", result.Duration.BottleneckResourceCode);
        Assert.True(result.Duration.WorkingDays >= 1);
        Assert.NotNull(result.Duration.MarginWorkingDays);
        Assert.True(result.Duration.MarginWorkingDays >= 0);
        Assert.Contains(result.Tasks, t => t.IsOnCriticalPath && t.IsBottleneck);
        Assert.All(result.Tasks.Where(t => t.Kind == ApsScheduleTaskKinds.Operation), t => Assert.NotEmpty(t.DependsOnTaskIds));
    }

    [Fact]
    public void Late_status_when_end_after_need_date()
    {
        var loads = new[]
        {
            new ApsLoadBucketResult("CC_REMAILLAGE", ApsResourceAlgebras.Labour, "HEURES_PERSONNE", Need.AddDays(2), 10, 0.2, 0, 2, ["t"])
        };
        var sats = new[] { SaturationEngine.Compute("CC_REMAILLAGE", From, Need.AddDays(5), 2, 20, 0.85) };
        var bn = BottleneckEngine.Identify(From, Need.AddDays(5), sats);

        var result = ApsScheduleDecisionEngine.Build(
            From, Need.AddDays(5), loads, sats, bn,
            needDate: Need,
            today: Today,
            finishedArticleCode: "DEMO_PF");

        Assert.Contains(result.Tasks, t => t.Status == ApsScheduleTaskStatuses.Late);
        Assert.True(result.Duration.MarginWorkingDays < 0);
    }

    [Fact]
    public void Csv_export_contains_header_and_rows()
    {
        var loads = new[]
        {
            new ApsLoadBucketResult("CC_TRICOTAGE", ApsResourceAlgebras.Machine, "HEURES_MACHINE", From, 10, 0.1, 0, 1, ["t"])
        };
        var sats = new[] { SaturationEngine.Compute("CC_TRICOTAGE", From, To, 1, 20, 0.85) };
        var bn = BottleneckEngine.Identify(From, To, sats);
        var result = ApsScheduleDecisionEngine.Build(From, To, loads, sats, bn, finishedArticleCode: "DEMO_PF");
        var csv = ApsScheduleDecisionEngine.BuildTasksCsv(result.Tasks);

        Assert.StartsWith("TaskId;Kind;Label", csv);
        Assert.Contains("OPERATION", csv);
        Assert.Contains("OF", csv);
    }

    [Fact]
    public void CountWorkingDays_skips_weekends()
    {
        // 2026-07-20 Monday → 2026-07-26 Sunday = 5 working days
        Assert.Equal(5, ApsScheduleDecisionEngine.CountWorkingDays(new(2026, 7, 20), new(2026, 7, 26)));
    }

    [Fact]
    public void Empty_loads_yield_zero_duration_window_start()
    {
        var sats = Array.Empty<ApsSaturationResult>();
        var bn = BottleneckEngine.Identify(From, To, sats);
        var result = ApsScheduleDecisionEngine.Build(From, To, [], sats, bn);
        Assert.Empty(result.Tasks);
        Assert.Equal(From, result.Duration.StartDate);
        Assert.Equal(1, result.Duration.WorkingDays); // same day weekday
    }
}

using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Axioplan.GammesNomenclatures.Domain.Aps.Schedule;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class ApsGanttLayoutEngineTests
{
    private static ApsScheduleDecisionResult BuildSchedule(
        DateOnly from,
        DateOnly to,
        DateOnly today,
        DateOnly? need,
        params (string Resource, DateOnly Start, DateOnly End, double Qty, bool Bn)[] ops)
    {
        var loads = ops.Select(o => new ApsLoadBucketResult(
            o.Resource,
            ApsResourceAlgebras.Machine,
            "HEURES_MACHINE",
            o.Start,
            o.Qty,
            0.1,
            0,
            Math.Max(1, o.Qty * 0.1),
            ["t"])).ToList();

        var sats = ops
            .Select(o => SaturationEngine.Compute(o.Resource, from, to, o.Bn ? 25 : 5, 20, 0.85))
            .ToList();
        var bn = BottleneckEngine.Identify(from, to, sats);
        var decision = ApsScheduleDecisionEngine.Build(
            from, to, loads, sats, bn, need, today, "CMD-1", "DEMO_PF");

        var patched = decision.Tasks.Select(t =>
        {
            if (t.Kind != ApsScheduleTaskKinds.Operation)
            {
                return t with { Start = ops.Min(o => o.Start), End = ops.Max(o => o.End) };
            }

            var match = ops.FirstOrDefault(o => o.Resource == t.ResourceCode && o.Start == t.Start);
            return match.Resource is null ? t : t with { End = match.End, IsBottleneck = match.Bn };
        }).ToList();

        var start = patched.Min(t => t.Start);
        var end = patched.Max(t => t.End);
        int? margin = null;
        if (need is not null)
        {
            margin = need.Value < end
                ? -ApsScheduleDecisionEngine.CountWorkingDays(need.Value.AddDays(1), end)
                : need.Value == end
                    ? 0
                    : ApsScheduleDecisionEngine.CountWorkingDays(end.AddDays(1), need.Value);
        }

        return decision with
        {
            Tasks = patched,
            Today = today,
            Duration = decision.Duration with
            {
                StartDate = start,
                EstimatedEndDate = end,
                WorkingDays = ApsScheduleDecisionEngine.CountWorkingDays(start, end),
                CalendarDays = end.DayNumber - start.DayNumber + 1,
                MarginWorkingDays = margin,
                NeedDate = need
            }
        };
    }

    [Fact]
    public void Hierarchy_has_of_operation_and_resource_rows()
    {
        var from = new DateOnly(2026, 7, 22);
        var schedule = BuildSchedule(from, from.AddDays(2), from, from.AddDays(14),
            ("CC_TRICOTAGE", from, from.AddDays(1), 10, true),
            ("CC_REMAILLAGE", from.AddDays(1), from.AddDays(1), 8, false));
        var layout = ApsGanttLayoutEngine.Compute(schedule, "Hierarchie");

        Assert.Contains(layout.Rows, r => r.Level == ApsGanttRowLevel.WorkOrder);
        Assert.Contains(layout.Rows, r => r.Level == ApsGanttRowLevel.Operation && r.DisplayName.Contains("Tricotage"));
        Assert.Contains(layout.Rows, r => r.Level == ApsGanttRowLevel.Resource && r.DisplayName.Contains("CC_TRICOTAGE"));
        Assert.False(ApsGanttLayoutEngine.RowsOverlapVertically(layout.Rows));
        Assert.All(layout.Rows.Where(r => r.Level == ApsGanttRowLevel.WorkOrder),
            r => Assert.True(r.RowHeight >= ApsGanttLayoutEngine.OfRowHeight));
        Assert.All(layout.Rows.Where(r => r.Level == ApsGanttRowLevel.Operation),
            r => Assert.True(r.RowHeight >= ApsGanttLayoutEngine.OperationRowHeight));
        Assert.All(layout.Rows.Where(r => r.Level == ApsGanttRowLevel.Resource),
            r => Assert.True(r.RowHeight >= ApsGanttLayoutEngine.ResourceRowHeight));
    }

    [Fact]
    public void Short_planning_uses_wide_day_and_min_bar_width()
    {
        var from = new DateOnly(2026, 7, 22);
        var schedule = BuildSchedule(from, from.AddDays(2), from, from.AddDays(21),
            ("CC_TRICOTAGE", from, from, 10, true),
            ("CC_REMAILLAGE", from.AddDays(1), from.AddDays(1), 8, false));

        var layout = ApsGanttLayoutEngine.Compute(schedule, "Test court");
        Assert.True(layout.DayWidth >= ApsGanttLayoutEngine.MinDayWidthPx);
        Assert.All(layout.Rows, r => Assert.True(r.Bar.Width >= ApsGanttLayoutEngine.MinBarWidthPx));
        Assert.False(ApsGanttLayoutEngine.TicksOverlap(layout.Ticks, 48));
        Assert.Contains("Aujourd", layout.TodayLabel ?? "");
        Assert.False(string.IsNullOrWhiteSpace(layout.FullSvgMarkup));
        Assert.False(string.IsNullOrWhiteSpace(layout.TimelineSvgMarkup));
        Assert.All(layout.Rows.Where(r => r.Bar.Width < 56), r => Assert.Equal(string.Empty, r.Bar.InnerText));
    }

    [Fact]
    public void Long_planning_keeps_min_day_width_and_scroll_friendly()
    {
        var from = new DateOnly(2026, 7, 1);
        var ops = Enumerable.Range(0, 8)
            .Select(i => ("CC_REMAILLAGE", from.AddDays(i * 4), from.AddDays(i * 4 + 1), 5.0, i == 3))
            .ToArray();
        var schedule = BuildSchedule(from, from.AddDays(35), from.AddDays(5), from.AddDays(40), ops);
        var layout = ApsGanttLayoutEngine.Compute(schedule, "Test long");
        Assert.True(layout.SpanDays >= 15);
        Assert.True(layout.DayWidth >= ApsGanttLayoutEngine.MinDayWidthPx);
        Assert.True(layout.TimelineWidth >= layout.SpanDays * ApsGanttLayoutEngine.MinDayWidthPx);
        Assert.False(ApsGanttLayoutEngine.TicksOverlap(layout.Ticks, 48));
    }

    [Fact]
    public void Status_column_is_single_badge_label_no_duplicate_goulot_word_in_name()
    {
        var from = new DateOnly(2026, 7, 22);
        var schedule = BuildSchedule(from, from.AddDays(1), from, from.AddDays(10),
            ("CC_TRICOTAGE", from, from, 12, true));
        var layout = ApsGanttLayoutEngine.Compute(schedule, "Statuts");
        var bottleneckRows = layout.Rows.Where(r => r.IsBottleneck).ToList();
        Assert.NotEmpty(bottleneckRows);
        Assert.All(bottleneckRows, r => Assert.Equal("Goulot", r.StatusLabel));
        Assert.All(layout.Rows, r => Assert.DoesNotContain("Goulot", r.DisplayName));
    }

    [Fact]
    public void Display_names_are_human_readable_not_raw_ids()
    {
        var from = new DateOnly(2026, 7, 22);
        var schedule = BuildSchedule(from, from.AddDays(3), from, from.AddDays(14),
            ("CC_TRICOTAGE", from, from.AddDays(1), 12, true));
        var layout = ApsGanttLayoutEngine.Compute(schedule, "Noms");
        Assert.Contains(layout.Rows, r => r.DisplayName.StartsWith("OF "));
        Assert.Contains(layout.Rows, r => r.DisplayName.Contains("Tricotage"));
        Assert.All(layout.CriticalPathLabels, l => Assert.DoesNotContain("OP-", l));
    }

    [Fact]
    public void Export_file_names_and_print_table_are_present()
    {
        var name = ApsGanttLayoutEngine.BuildExportFileName("png", "DEMO_PF / A", new DateOnly(2026, 7, 22), "png");
        Assert.Equal("Planning_APS_DEMO_PF___A_2026-07-22.png", name);
        var csv = ApsGanttLayoutEngine.BuildExportFileName("csv", "DEMO_PF", new DateOnly(2026, 7, 22), "csv");
        Assert.Equal("Taches_APS_DEMO_PF_2026-07-22.csv", csv);

        var from = new DateOnly(2026, 7, 22);
        var schedule = BuildSchedule(from, from.AddDays(1), from, null, ("CC_TRICOTAGE", from, from, 1, false));
        var layout = ApsGanttLayoutEngine.Compute(schedule, "Table");
        Assert.Contains("<table", layout.TasksTableHtml);
        Assert.Contains("Élément", layout.TasksTableHtml);
        Assert.Contains("Statut", layout.TasksTableHtml);
    }

    [Fact]
    public void Margin_and_duration_match_summary_dates()
    {
        var start = new DateOnly(2026, 7, 22);
        var end = new DateOnly(2026, 7, 23);
        var need = new DateOnly(2026, 8, 12);
        var schedule = BuildSchedule(start, end, start, need,
            ("CC_TRICOTAGE", start, end, 10, true));
        Assert.Equal(start, schedule.Duration.StartDate);
        Assert.Equal(end, schedule.Duration.EstimatedEndDate);
        Assert.Equal(2, schedule.Duration.WorkingDays);
        Assert.Equal(ApsScheduleDecisionEngine.CountWorkingDays(end.AddDays(1), need), schedule.Duration.MarginWorkingDays);
        var layout = ApsGanttLayoutEngine.Compute(schedule, "Cohérence");
        Assert.False(ApsGanttLayoutEngine.RowsOverlapVertically(layout.Rows));
        Assert.All(layout.Rows, r => Assert.True(r.Bar.Y >= layout.HeaderHeight));
        Assert.True(layout.HeaderHeight >= ApsGanttLayoutEngine.TitleBandHeight + ApsGanttLayoutEngine.DateBandHeight);
    }

    [Fact]
    public void Multiple_of_rows_are_supported()
    {
        var from = new DateOnly(2026, 7, 22);
        var loads = new[]
        {
            new ApsLoadBucketResult("CC_TRICOTAGE", ApsResourceAlgebras.Machine, "HEURES_MACHINE", from, 10, 0.1, 0, 1, ["t"]),
            new ApsLoadBucketResult("CC_REMAILLAGE", ApsResourceAlgebras.Labour, "HEURES_PERSONNE", from.AddDays(1), 8, 0.2, 0, 1.6, ["t"])
        };
        var sats = new[]
        {
            SaturationEngine.Compute("CC_TRICOTAGE", from, from.AddDays(5), 10, 20, 0.85),
            SaturationEngine.Compute("CC_REMAILLAGE", from, from.AddDays(5), 8, 20, 0.85)
        };
        var bn = BottleneckEngine.Identify(from, from.AddDays(5), sats);
        var baseDecision = ApsScheduleDecisionEngine.Build(from, from.AddDays(5), loads, sats, bn, null, from, "A", "PF_A");
        var tasks = new List<ApsScheduleTask>
        {
            new("OF-A", ApsScheduleTaskKinds.WorkOrder, "OF A", null, null, "PF_A", from, from.AddDays(1), 10, 1,
                ApsScheduleTaskStatuses.Planned, false, true, [], "A"),
            new("OP-A", ApsScheduleTaskKinds.Operation, "op", "OF-A", "CC_TRICOTAGE", "PF_A", from, from, 10, 1,
                ApsScheduleTaskStatuses.InProgress, true, true, ["OF-A"], "A"),
            new("OF-B", ApsScheduleTaskKinds.WorkOrder, "OF B", null, null, "PF_B", from.AddDays(1), from.AddDays(2), 5, 1,
                ApsScheduleTaskStatuses.Planned, false, false, [], "B"),
            new("OP-B", ApsScheduleTaskKinds.Operation, "op", "OF-B", "CC_REMAILLAGE", "PF_B", from.AddDays(1), from.AddDays(1), 5, 1,
                ApsScheduleTaskStatuses.Planned, false, false, ["OF-B"], "B")
        };
        var schedule = baseDecision with { Tasks = tasks };
        var layout = ApsGanttLayoutEngine.Compute(schedule, "Multi OF");
        Assert.Equal(2, layout.Rows.Count(r => r.Level == ApsGanttRowLevel.WorkOrder));
        Assert.True(layout.Rows.Count(r => r.Level == ApsGanttRowLevel.Operation) >= 2);
        Assert.True(layout.Rows.Count(r => r.Level == ApsGanttRowLevel.Resource) >= 2);
        Assert.False(ApsGanttLayoutEngine.RowsOverlapVertically(layout.Rows));
    }
}

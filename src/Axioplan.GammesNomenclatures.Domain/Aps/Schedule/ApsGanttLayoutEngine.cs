using System.Globalization;
using System.Text;

namespace Axioplan.GammesNomenclatures.Domain.Aps.Schedule;

public sealed record ApsGanttTick(DateOnly Date, double CenterX, string Label, bool IsMajor);

public sealed record ApsGanttBarView(
    string TaskId,
    double X,
    double Y,
    double Width,
    double Height,
    string Fill,
    string Stroke,
    double StrokeWidth,
    string InnerText,
    string Tooltip,
    bool IsBottleneck,
    bool IsCritical,
    double CenterX,
    double CenterY);

public sealed record ApsGanttDependencyView(
    string FromTaskId,
    string ToTaskId,
    double X1,
    double Y1,
    double X2,
    double Y2);

public enum ApsGanttRowLevel
{
    WorkOrder = 1,
    Operation = 2,
    Resource = 3
}

public sealed record ApsGanttRowView(
    string TaskId,
    string Kind,
    ApsGanttRowLevel Level,
    string DisplayName,
    string? ResourceCode,
    string QuantityText,
    string StatusLabel,
    string StatusCss,
    string StartText,
    string EndText,
    string DurationText,
    bool IsOf,
    bool IsCritical,
    bool IsBottleneck,
    int Indent,
    double RowY,
    double RowHeight,
    ApsGanttBarView Bar);

public sealed record ApsGanttLayoutResult(
    DateOnly TimelineStart,
    DateOnly TimelineEnd,
    int SpanDays,
    double DayWidth,
    int TickStepDays,
    double LeftPanelWidth,
    double HeaderHeight,
    double TitleBandHeight,
    double DateBandHeight,
    double ChartWidth,
    double TimelineWidth,
    double ChartHeight,
    double TodayX,
    string? TodayLabel,
    double TodayLabelX,
    IReadOnlyList<ApsGanttTick> Ticks,
    IReadOnlyList<ApsGanttRowView> Rows,
    IReadOnlyList<ApsGanttDependencyView> Dependencies,
    IReadOnlyList<string> CriticalPathLabels,
    string FullSvgMarkup,
    string TimelineSvgMarkup,
    string TasksTableHtml);

/// <summary>
/// Layout Gantt professionnel : hiérarchie OF → opération → ressource,
/// colonnes séparées, échelle journalière non compressée.
/// </summary>
public static class ApsGanttLayoutEngine
{
    public const double LeftPanelWidthPx = 560;
    public const double MinBarWidthPx = 36;
    public const double MinDayWidthPx = 110;
    public const double PreferredDayWidthShort = 130;
    public const double BarHeightPx = 26;
    public const double OfRowHeight = 52;
    public const double OperationRowHeight = 48;
    public const double ResourceRowHeight = 44;
    public const double TitleBandHeight = 28;
    public const double DateBandHeight = 36;
    public const double MarginDaysBefore = 1;
    public const double MarginDaysAfter = 2;

    public static ApsGanttLayoutResult Compute(
        ApsScheduleDecisionResult schedule,
        string panelTitle,
        string svgId = "aps-gantt-svg",
        double leftPanelWidth = LeftPanelWidthPx)
    {
        var sourceTasks = schedule.Tasks
            .OrderBy(t => t.Kind == ApsScheduleTaskKinds.WorkOrder ? 0 : 1)
            .ThenBy(t => t.Start)
            .ThenBy(t => t.ResourceCode)
            .ThenBy(t => t.TaskId)
            .ToList();

        var expanded = ExpandHierarchy(sourceTasks);

        var dataStart = expanded.Count == 0 ? schedule.Duration.StartDate : expanded.Min(t => t.Start);
        var dataEnd = expanded.Count == 0 ? schedule.Duration.EstimatedEndDate : expanded.Max(t => t.End);
        if (dataEnd < dataStart)
        {
            dataEnd = dataStart;
        }

        var timelineStart = dataStart.AddDays(-(int)MarginDaysBefore);
        var timelineEnd = dataEnd.AddDays((int)MarginDaysAfter);
        if (schedule.Duration.NeedDate is DateOnly need && need > timelineEnd && need <= dataEnd.AddDays(5))
        {
            timelineEnd = need;
        }

        var spanDays = Math.Max(1, timelineEnd.DayNumber - timelineStart.DayNumber + 1);
        var dayWidth = ResolveDayWidth(spanDays);
        var tickStep = ResolveTickStep(spanDays);
        var headerHeight = TitleBandHeight + DateBandHeight;
        var timelineWidth = spanDays * dayWidth + 24;

        double y = headerHeight;
        var rows = new List<ApsGanttRowView>();
        var barsById = new Dictionary<string, ApsGanttBarView>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in expanded)
        {
            var rowH = item.Level switch
            {
                ApsGanttRowLevel.WorkOrder => OfRowHeight,
                ApsGanttRowLevel.Operation => OperationRowHeight,
                _ => ResourceRowHeight
            };
            var bar = BuildBar(item, y, rowH, timelineStart, 0, dayWidth, schedule.Duration.MaxLoadRate);
            barsById[item.TaskId] = bar;
            rows.Add(new ApsGanttRowView(
                item.TaskId,
                item.Kind,
                item.Level,
                item.DisplayName,
                item.ResourceCode,
                item.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
                StatusLabel(item.Status, item.IsBottleneck),
                StatusCss(item.Status, item.IsBottleneck),
                item.Start.ToString("dd/MM"),
                item.End.ToString("dd/MM"),
                $"{CountInclusiveWorkingDays(item.Start, item.End)} j",
                item.Level == ApsGanttRowLevel.WorkOrder,
                item.IsOnCriticalPath,
                item.IsBottleneck,
                (int)item.Level - 1,
                y,
                rowH,
                bar));
            y += rowH;
        }

        var chartHeight = y + 8;
        var chartWidth = leftPanelWidth + timelineWidth;

        var ticks = BuildTicks(timelineStart, timelineEnd, dayWidth, 0, tickStep);
        var todayX = XForDateCenter(schedule.Today, timelineStart, 0, dayWidth);
        string? todayLabel = null;
        var todayLabelX = todayX;
        if (schedule.Today >= timelineStart && schedule.Today <= timelineEnd)
        {
            todayLabel = "Aujourd'hui";
            todayLabelX = AvoidOverlap(todayX, ticks, minGap: 70);
        }

        var deps = BuildDependencies(expanded, barsById);
        var byId = sourceTasks.ToDictionary(t => t.TaskId, StringComparer.OrdinalIgnoreCase);
        var criticalLabels = schedule.Duration.CriticalPathTaskIds
            .Select(id => byId.TryGetValue(id, out var t) ? BuildDisplayName(t) : id)
            .ToList();

        var timelineSvg = BuildSvg(
            svgId + "-timeline",
            panelTitle,
            timelineWidth,
            chartHeight,
            0,
            headerHeight,
            timelineStart,
            timelineEnd,
            dayWidth,
            ticks,
            schedule.Today,
            todayX,
            todayLabel,
            todayLabelX,
            rows,
            deps,
            schedule.Duration.NeedDate,
            embedLeftLabels: false);

        // Full export SVG: left labels drawn in SVG for self-contained export
        var fullRows = rows.Select(r =>
        {
            var b = r.Bar;
            var nx = b.X + leftPanelWidth;
            return r with
            {
                Bar = b with { X = nx, CenterX = nx + b.Width / 2 }
            };
        }).ToList();
        var fullDeps = deps.Select(d => d with
        {
            X1 = d.X1 + leftPanelWidth,
            X2 = d.X2 + leftPanelWidth
        }).ToList();
        var fullTicks = BuildTicks(timelineStart, timelineEnd, dayWidth, leftPanelWidth, tickStep);
        var fullTodayX = todayX + leftPanelWidth;
        var fullTodayLabelX = todayLabelX + leftPanelWidth;

        var fullSvg = BuildSvg(
            svgId,
            panelTitle,
            chartWidth,
            chartHeight,
            leftPanelWidth,
            headerHeight,
            timelineStart,
            timelineEnd,
            dayWidth,
            fullTicks,
            schedule.Today,
            fullTodayX,
            todayLabel,
            fullTodayLabelX,
            fullRows,
            fullDeps,
            schedule.Duration.NeedDate,
            embedLeftLabels: true);

        var tableHtml = BuildTasksTableHtml(rows);

        return new ApsGanttLayoutResult(
            timelineStart,
            timelineEnd,
            spanDays,
            dayWidth,
            tickStep,
            leftPanelWidth,
            headerHeight,
            TitleBandHeight,
            DateBandHeight,
            chartWidth,
            timelineWidth,
            chartHeight,
            todayX,
            todayLabel,
            todayLabelX,
            ticks,
            rows,
            deps,
            criticalLabels,
            fullSvg,
            timelineSvg,
            tableHtml);
    }

    public static double ResolveDayWidth(int spanDays)
    {
        // Jamais sous 110 px : scroll horizontal plutôt que compression.
        if (spanDays <= 7)
        {
            return PreferredDayWidthShort;
        }

        if (spanDays <= 14)
        {
            return 120;
        }

        return MinDayWidthPx;
    }

    public static int ResolveTickStep(int spanDays)
    {
        if (spanDays <= 21)
        {
            return 1;
        }

        if (spanDays <= 45)
        {
            return 2;
        }

        return 7;
    }

    public static bool TicksOverlap(IReadOnlyList<ApsGanttTick> ticks, double minGapPx = 48)
    {
        for (var i = 1; i < ticks.Count; i++)
        {
            if (Math.Abs(ticks[i].CenterX - ticks[i - 1].CenterX) < minGapPx)
            {
                return true;
            }
        }

        return false;
    }

    public static bool RowsOverlapVertically(IReadOnlyList<ApsGanttRowView> rows)
    {
        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].RowY < rows[i - 1].RowY + rows[i - 1].RowHeight - 0.01)
            {
                return true;
            }
        }

        return false;
    }

    public static string SanitizeFileToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "PLAN";
        }

        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var s = new string(chars).Trim('_');
        return string.IsNullOrEmpty(s) ? "PLAN" : s;
    }

    public static string BuildExportFileName(string kind, string? articleOrTitle, DateOnly date, string extension)
    {
        var token = SanitizeFileToken(articleOrTitle);
        var prefix = kind.Equals("csv", StringComparison.OrdinalIgnoreCase) ? "Taches_APS" : "Planning_APS";
        return $"{prefix}_{token}_{date:yyyy-MM-dd}.{extension.TrimStart('.')}";
    }

    public static string BuildDisplayName(ApsScheduleTask task)
    {
        if (task.Kind == ApsScheduleTaskKinds.WorkOrder)
        {
            return $"OF {task.ArticleCode ?? task.Label}";
        }

        return HumanizeResource(task.ResourceCode ?? task.Label);
    }

    private sealed record ExpandedItem(
        string TaskId,
        string Kind,
        ApsGanttRowLevel Level,
        string DisplayName,
        string? ResourceCode,
        string? ArticleCode,
        DateOnly Start,
        DateOnly End,
        double Quantity,
        double Charge,
        string Status,
        bool IsBottleneck,
        bool IsOnCriticalPath,
        IReadOnlyList<string> DependsOnTaskIds,
        string? ParentTaskId);

    private static List<ExpandedItem> ExpandHierarchy(IReadOnlyList<ApsScheduleTask> tasks)
    {
        var list = new List<ExpandedItem>();
        var ofs = tasks.Where(t => t.Kind == ApsScheduleTaskKinds.WorkOrder).ToList();
        var ops = tasks.Where(t => t.Kind != ApsScheduleTaskKinds.WorkOrder).ToList();

        if (ofs.Count == 0 && ops.Count > 0)
        {
            // Fallback synthetic OF
            var start = ops.Min(o => o.Start);
            var end = ops.Max(o => o.End);
            ofs.Add(new ApsScheduleTask(
                "OF-SYNTH", ApsScheduleTaskKinds.WorkOrder, "OF", null, null,
                ops[0].ArticleCode ?? "PF", start, end, ops.Sum(o => o.Quantity), ops.Sum(o => o.Charge),
                ApsScheduleTaskStatuses.Planned, false, true, [], ops[0].OrderId));
        }

        foreach (var of in ofs)
        {
            list.Add(ToExpanded(of, ApsGanttRowLevel.WorkOrder, BuildDisplayName(of), of.ResourceCode));
            var children = ops
                .Where(o => o.ParentTaskId == of.TaskId
                            || (o.ParentTaskId is null && ofs.Count == 1)
                            || string.Equals(o.ArticleCode, of.ArticleCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (children.Count == 0 && ofs.Count == 1)
            {
                children = ops;
            }

            foreach (var op in children)
            {
                var opName = HumanizeResource(op.ResourceCode ?? op.Label);
                list.Add(ToExpanded(
                    op with { TaskId = op.TaskId + "::op", Kind = ApsScheduleTaskKinds.Operation },
                    ApsGanttRowLevel.Operation,
                    opName,
                    op.ResourceCode));
                list.Add(ToExpanded(
                    op with { TaskId = op.TaskId + "::res", Kind = ApsScheduleTaskKinds.ResourceBucket },
                    ApsGanttRowLevel.Resource,
                    op.ResourceCode ?? opName,
                    op.ResourceCode));
            }
        }

        // Orphan ops
        var linked = new HashSet<string>(list.Select(x => x.TaskId.Split("::")[0]), StringComparer.OrdinalIgnoreCase);
        foreach (var op in ops.Where(o => !linked.Contains(o.TaskId)))
        {
            var opName = HumanizeResource(op.ResourceCode ?? op.Label);
            list.Add(ToExpanded(op with { TaskId = op.TaskId + "::op" }, ApsGanttRowLevel.Operation, opName, op.ResourceCode));
            list.Add(ToExpanded(op with { TaskId = op.TaskId + "::res" }, ApsGanttRowLevel.Resource, op.ResourceCode ?? opName, op.ResourceCode));
        }

        return list;
    }

    private static ExpandedItem ToExpanded(
        ApsScheduleTask task,
        ApsGanttRowLevel level,
        string displayName,
        string? resourceCode)
        => new(
            task.TaskId,
            task.Kind,
            level,
            displayName,
            resourceCode,
            task.ArticleCode,
            task.Start,
            task.End,
            task.Quantity,
            task.Charge,
            task.Status,
            task.IsBottleneck,
            task.IsOnCriticalPath,
            task.DependsOnTaskIds,
            task.ParentTaskId);

    private static List<ApsGanttDependencyView> BuildDependencies(
        IReadOnlyList<ExpandedItem> items,
        IReadOnlyDictionary<string, ApsGanttBarView> barsById)
    {
        var deps = new List<ApsGanttDependencyView>();
        // Link resource rows in chronological order within same OF family.
        var resources = items.Where(i => i.Level == ApsGanttRowLevel.Resource).OrderBy(i => i.Start).ThenBy(i => i.TaskId).ToList();
        for (var i = 1; i < resources.Count; i++)
        {
            var prev = resources[i - 1];
            var cur = resources[i];
            if (!barsById.TryGetValue(prev.TaskId, out var from) || !barsById.TryGetValue(cur.TaskId, out var to))
            {
                continue;
            }

            deps.Add(new ApsGanttDependencyView(
                prev.TaskId, cur.TaskId,
                from.X + from.Width, from.CenterY,
                to.X, to.CenterY));
        }

        return deps;
    }

    private static ApsGanttBarView BuildBar(
        ExpandedItem task,
        double rowY,
        double rowHeight,
        DateOnly timelineStart,
        double labelWidth,
        double dayWidth,
        double? maxLoadRate)
    {
        var startOffset = Math.Max(0, task.Start.DayNumber - timelineStart.DayNumber);
        var durationDays = Math.Max(1, task.End.DayNumber - task.Start.DayNumber + 1);
        var x = labelWidth + startOffset * dayWidth + 6;
        var naturalW = durationDays * dayWidth - 12;
        var w = Math.Max(MinBarWidthPx, naturalW);
        var h = BarHeightPx;
        var y = rowY + (rowHeight - h) / 2.0;
        var fill = StatusColor(task.Status);
        var stroke = task.IsOnCriticalPath ? "#9a3412" : (task.IsBottleneck ? "#c2410c" : "#1e293b");
        var strokeW = task.IsOnCriticalPath ? 2.6 : 1.0;
        var inner = ResolveInnerText(task, w);
        var tip = BuildTooltip(task, maxLoadRate);
        return new ApsGanttBarView(
            task.TaskId, x, y, w, h, fill, stroke, strokeW, inner, tip,
            task.IsBottleneck, task.IsOnCriticalPath, x + w / 2, y + h / 2);
    }

    private static string ResolveInnerText(ExpandedItem task, double barWidth)
    {
        // Texte interne uniquement si la barre est assez large ; sinon tooltip seule.
        if (barWidth < 56)
        {
            return string.Empty;
        }

        if (barWidth < 90)
        {
            return Truncate(task.DisplayName, 4);
        }

        if (barWidth < 140)
        {
            return Truncate(task.DisplayName, 10);
        }

        return Truncate(task.DisplayName, (int)(barWidth / 8));
    }

    private static string BuildTooltip(ExpandedItem t, double? maxLoadRate)
    {
        var sb = new StringBuilder();
        sb.Append("OF / article : ").Append(t.ArticleCode ?? "-").AppendLine();
        sb.Append("Élément : ").Append(t.DisplayName).AppendLine();
        sb.Append("Niveau : ").Append(t.Level).AppendLine();
        sb.Append("Ressource : ").Append(t.ResourceCode ?? "-").AppendLine();
        sb.Append("Quantité : ").Append(t.Quantity.ToString("0.##", CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Statut : ").Append(StatusLabel(t.Status, t.IsBottleneck)).AppendLine();
        sb.Append("Début : ").Append(t.Start.ToString("dd/MM/yyyy")).AppendLine();
        sb.Append("Fin : ").Append(t.End.ToString("dd/MM/yyyy")).AppendLine();
        sb.Append("Durée : ").Append(CountInclusiveWorkingDays(t.Start, t.End)).Append(" j ouvrés").AppendLine();
        sb.Append("Charge : ").Append(t.Charge.ToString("0.##", CultureInfo.InvariantCulture)).AppendLine();
        if (maxLoadRate is not null)
        {
            sb.Append("Taux de charge max (rho) : ").Append(maxLoadRate.Value.ToString("P1")).AppendLine();
        }

        sb.Append("Goulot : ").Append(t.IsBottleneck ? "oui" : "non").AppendLine();
        sb.Append("Chemin critique : ").Append(t.IsOnCriticalPath ? "oui" : "non");
        return sb.ToString();
    }

    private static string StatusLabel(string status, bool isBottleneck)
    {
        if (isBottleneck)
        {
            return "Goulot";
        }

        return status switch
        {
            ApsScheduleTaskStatuses.Late => "Retard",
            ApsScheduleTaskStatuses.Saturated => "Saturé",
            ApsScheduleTaskStatuses.InProgress => "En cours",
            ApsScheduleTaskStatuses.Done => "Terminé",
            ApsScheduleTaskStatuses.External => "Externe",
            _ => "Planifié"
        };
    }

    private static string StatusCss(string status, bool isBottleneck)
    {
        if (isBottleneck)
        {
            return "goulot";
        }

        return status switch
        {
            ApsScheduleTaskStatuses.Late => "late",
            ApsScheduleTaskStatuses.Saturated => "sat",
            ApsScheduleTaskStatuses.InProgress => "progress",
            ApsScheduleTaskStatuses.Done => "done",
            _ => "plan"
        };
    }

    private static string StatusColor(string status) => status switch
    {
        ApsScheduleTaskStatuses.Late => "#dc2626",
        ApsScheduleTaskStatuses.Saturated => "#ea580c",
        ApsScheduleTaskStatuses.InProgress => "#2563eb",
        ApsScheduleTaskStatuses.Done => "#16a34a",
        ApsScheduleTaskStatuses.External => "#64748b",
        _ => "#0f766e"
    };

    private static string HumanizeResource(string code)
    {
        if (code.Contains("TRICOT", StringComparison.OrdinalIgnoreCase))
        {
            return "Tricotage";
        }

        if (code.Contains("REMAIL", StringComparison.OrdinalIgnoreCase))
        {
            return "Remaillage";
        }

        if (code.Contains("TRAIT", StringComparison.OrdinalIgnoreCase))
        {
            return "Traitement";
        }

        if (code.Contains("EXPED", StringComparison.OrdinalIgnoreCase))
        {
            return "Expédition";
        }

        return code.StartsWith("CC_", StringComparison.OrdinalIgnoreCase) ? code[3..] : code;
    }

    private static int CountInclusiveWorkingDays(DateOnly from, DateOnly to)
        => ApsScheduleDecisionEngine.CountWorkingDays(from, to);

    private static List<ApsGanttTick> BuildTicks(
        DateOnly start,
        DateOnly end,
        double dayWidth,
        double labelWidth,
        int step)
    {
        var ticks = new List<ApsGanttTick>();
        for (var d = start; d <= end; d = d.AddDays(step))
        {
            var center = XForDateCenter(d, start, labelWidth, dayWidth);
            var label = step >= 7
                ? d.ToString("dd MMM", CultureInfo.GetCultureInfo("fr-FR"))
                : d.ToString("dd/MM");
            ticks.Add(new ApsGanttTick(d, center, label, d.DayOfWeek == DayOfWeek.Monday || d == start));
        }

        if (ticks.Count == 0 || ticks[^1].Date != end)
        {
            var center = XForDateCenter(end, start, labelWidth, dayWidth);
            if (ticks.Count == 0 || center - ticks[^1].CenterX >= 55)
            {
                ticks.Add(new ApsGanttTick(end, center, end.ToString("dd/MM"), true));
            }
        }

        return ticks;
    }

    private static double XForDateCenter(DateOnly date, DateOnly timelineStart, double labelWidth, double dayWidth)
        => labelWidth + (date.DayNumber - timelineStart.DayNumber) * dayWidth + dayWidth / 2.0;

    private static double AvoidOverlap(double preferredX, IReadOnlyList<ApsGanttTick> ticks, double minGap)
    {
        var x = preferredX;
        foreach (var tick in ticks.OrderBy(t => Math.Abs(t.CenterX - preferredX)))
        {
            if (Math.Abs(x - tick.CenterX) < minGap)
            {
                x = tick.CenterX + minGap;
            }
        }

        return x;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..Math.Max(1, max - 1)] + "…";

    private static string BuildTasksTableHtml(IReadOnlyList<ApsGanttRowView> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<table class=\"gantt-print-table\"><thead><tr>");
        sb.Append("<th>Élément</th><th>Qté</th><th>Statut</th><th>Début</th><th>Fin</th><th>Durée</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var r in rows)
        {
            var pad = new string(' ', r.Indent * 4);
            sb.Append("<tr>")
                .Append("<td>").Append(pad).Append(Escape(r.DisplayName)).Append("</td>")
                .Append("<td>").Append(Escape(r.QuantityText)).Append("</td>")
                .Append("<td>").Append(Escape(r.StatusLabel)).Append("</td>")
                .Append("<td>").Append(Escape(r.StartText)).Append("</td>")
                .Append("<td>").Append(Escape(r.EndText)).Append("</td>")
                .Append("<td>").Append(Escape(r.DurationText)).Append("</td>")
                .Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static string BuildSvg(
        string svgId,
        string panelTitle,
        double width,
        double height,
        double leftWidth,
        double headerHeight,
        DateOnly timelineStart,
        DateOnly timelineEnd,
        double dayWidth,
        IReadOnlyList<ApsGanttTick> ticks,
        DateOnly today,
        double todayX,
        string? todayLabel,
        double todayLabelX,
        IReadOnlyList<ApsGanttRowView> rows,
        IReadOnlyList<ApsGanttDependencyView> deps,
        DateOnly? needDate,
        bool embedLeftLabels)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(inv, $"<svg xmlns=\"http://www.w3.org/2000/svg\" id=\"{Escape(svgId)}\" class=\"aps-gantt-svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" role=\"img\">");
        sb.Append("<defs><style type=\"text/css\">");
        sb.Append(".ttl{font:600 13px 'Segoe UI',sans-serif;fill:#0f172a}");
        sb.Append(".date{font:11px 'Segoe UI',sans-serif;fill:#475569;text-anchor:middle}");
        sb.Append(".today{font:600 10px 'Segoe UI',sans-serif;fill:#b91c1c;text-anchor:middle}");
        sb.Append(".need{font:600 10px 'Segoe UI',sans-serif;fill:#7c3aed;text-anchor:middle}");
        sb.Append(".lbl{font:12px 'Segoe UI',sans-serif;fill:#0f172a}");
        sb.Append(".lbl-of{font:600 12px 'Segoe UI',sans-serif;fill:#0f172a}");
        sb.Append(".lbl-res{font:11px 'Segoe UI',sans-serif;fill:#64748b}");
        sb.Append(".meta{font:10px 'Segoe UI',sans-serif;fill:#64748b;text-anchor:middle}");
        sb.Append(".bar{font:600 10px 'Segoe UI',sans-serif;fill:#fff;text-anchor:middle;dominant-baseline:middle}");
        sb.Append(".of-bg{fill:#eff6ff}.op-bg{fill:#ffffff}.res-bg{fill:#f8fafc}.alt{fill:#f1f5f9}");
        sb.Append("</style>");
        sb.Append("<marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"8\" refY=\"5\" markerWidth=\"6\" markerHeight=\"6\" orient=\"auto-start-reverse\">");
        sb.Append("<path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#94a3b8\"/></marker>");
        sb.Append("<clipPath id=\"titleClip\"><rect x=\"0\" y=\"0\" width=\"100%\" height=\"").Append(TitleBandHeight.ToString(inv)).Append("\"/></clipPath>");
        sb.Append("</defs>");

        sb.Append(inv, $"<rect width=\"{width}\" height=\"{height}\" fill=\"#ffffff\"/>");

        // Title band (export) — separate from date band / today label
        if (embedLeftLabels)
        {
            sb.Append(inv, $"<text class=\"ttl\" x=\"12\" y=\"18\">{Escape(panelTitle)}</text>");
        }

        // Weekend bands under date header only
        for (var d = timelineStart; d <= timelineEnd; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                var x = leftWidth + (d.DayNumber - timelineStart.DayNumber) * dayWidth;
                sb.Append(inv, $"<rect x=\"{x}\" y=\"{headerHeight}\" width=\"{dayWidth}\" height=\"{height - headerHeight}\" fill=\"#f1f5f9\"/>");
            }
        }

        // Date header background
        sb.Append(inv, $"<rect x=\"{leftWidth}\" y=\"{TitleBandHeight}\" width=\"{width - leftWidth}\" height=\"{DateBandHeight}\" fill=\"#f8fafc\"/>");

        foreach (var tick in ticks)
        {
            sb.Append(inv, $"<line x1=\"{tick.CenterX}\" y1=\"{headerHeight}\" x2=\"{tick.CenterX}\" y2=\"{height}\" stroke=\"#e2e8f0\"/>");
            sb.Append(inv, $"<text class=\"date\" x=\"{tick.CenterX}\" y=\"{TitleBandHeight + 22}\">{Escape(tick.Label)}</text>");
        }

        // Today: line only below header; label only in title band
        if (todayLabel is not null && today >= timelineStart && today <= timelineEnd)
        {
            sb.Append(inv, $"<line x1=\"{todayX}\" y1=\"{headerHeight}\" x2=\"{todayX}\" y2=\"{height}\" stroke=\"#dc2626\" stroke-width=\"2\" stroke-dasharray=\"5 4\"/>");
            sb.Append(inv, $"<rect x=\"{todayLabelX - 38}\" y=\"4\" width=\"76\" height=\"18\" rx=\"4\" fill=\"#fef2f2\" stroke=\"#fecaca\"/>");
            sb.Append(inv, $"<text class=\"today\" x=\"{todayLabelX}\" y=\"17\">{Escape(todayLabel)}</text>");
        }

        if (needDate is DateOnly need && need >= timelineStart && need <= timelineEnd)
        {
            var needX = XForDateCenter(need, timelineStart, leftWidth, dayWidth);
            var needLabelX = AvoidOverlap(needX, ticks, 60);
            if (todayLabel is not null && Math.Abs(needLabelX - todayLabelX) < 60)
            {
                needLabelX = todayLabelX + 70;
            }

            sb.Append(inv, $"<line x1=\"{needX}\" y1=\"{headerHeight}\" x2=\"{needX}\" y2=\"{height}\" stroke=\"#7c3aed\" stroke-width=\"1.5\" stroke-dasharray=\"3 3\"/>");
            sb.Append(inv, $"<text class=\"need\" x=\"{needLabelX}\" y=\"{TitleBandHeight + 12}\">Besoin</text>");
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var bg = row.Level switch
            {
                ApsGanttRowLevel.WorkOrder => "of-bg",
                ApsGanttRowLevel.Resource => i % 2 == 0 ? "res-bg" : "alt",
                _ => "op-bg"
            };
            sb.Append(inv, $"<rect class=\"{bg}\" x=\"0\" y=\"{row.RowY}\" width=\"{width}\" height=\"{row.RowHeight}\"/>");

            if (embedLeftLabels && leftWidth > 0)
            {
                var indent = 12 + row.Indent * 16;
                var cls = row.Level == ApsGanttRowLevel.WorkOrder ? "lbl-of"
                    : row.Level == ApsGanttRowLevel.Resource ? "lbl-res" : "lbl";
                sb.Append(inv, $"<text class=\"{cls}\" x=\"{indent}\" y=\"{row.RowY + row.RowHeight / 2 + 4}\">{Escape(Truncate(row.DisplayName, 26))}</text>");
                sb.Append(inv, $"<text class=\"meta\" x=\"{leftWidth - 200}\" y=\"{row.RowY + row.RowHeight / 2 + 4}\">{Escape(row.QuantityText)}</text>");
                sb.Append(inv, $"<text class=\"meta\" x=\"{leftWidth - 140}\" y=\"{row.RowY + row.RowHeight / 2 + 4}\">{Escape(row.StatusLabel)}</text>");
                sb.Append(inv, $"<text class=\"meta\" x=\"{leftWidth - 80}\" y=\"{row.RowY + row.RowHeight / 2 + 4}\">{Escape(row.StartText)}</text>");
                sb.Append(inv, $"<text class=\"meta\" x=\"{leftWidth - 40}\" y=\"{row.RowY + row.RowHeight / 2 + 4}\">{Escape(row.EndText)}</text>");
            }

            var bar = row.Bar;
            var clipId = "c" + Math.Abs(bar.TaskId.GetHashCode(StringComparison.Ordinal)).ToString(inv);
            sb.Append(inv, $"<clipPath id=\"{clipId}\"><rect x=\"{bar.X}\" y=\"{bar.Y}\" width=\"{bar.Width}\" height=\"{bar.Height}\" rx=\"5\"/></clipPath>");
            sb.Append(inv,
                $"<rect x=\"{bar.X}\" y=\"{bar.Y}\" width=\"{bar.Width}\" height=\"{bar.Height}\" rx=\"5\" fill=\"{bar.Fill}\" stroke=\"{bar.Stroke}\" stroke-width=\"{bar.StrokeWidth.ToString(inv)}\" opacity=\"0.95\">");
            sb.Append(inv, $"<title>{Escape(bar.Tooltip)}</title></rect>");
            if (!string.IsNullOrEmpty(bar.InnerText))
            {
                sb.Append(inv, $"<text class=\"bar\" clip-path=\"url(#{clipId})\" x=\"{bar.CenterX}\" y=\"{bar.CenterY}\">{Escape(bar.InnerText)}</text>");
            }
        }

        foreach (var dep in deps)
        {
            var midX = (dep.X1 + dep.X2) / 2;
            sb.Append(inv,
                $"<path d=\"M {dep.X1} {dep.Y1} C {midX} {dep.Y1}, {midX} {dep.Y2}, {dep.X2} {dep.Y2}\" fill=\"none\" stroke=\"#94a3b8\" stroke-width=\"1.2\" marker-end=\"url(#arrow)\"/>");
        }

        if (leftWidth > 0)
        {
            sb.Append(inv, $"<line x1=\"{leftWidth}\" y1=\"{headerHeight}\" x2=\"{leftWidth}\" y2=\"{height}\" stroke=\"#94a3b8\" stroke-width=\"1.5\"/>");
        }

        sb.Append(inv, $"<line x1=\"0\" y1=\"{headerHeight}\" x2=\"{width}\" y2=\"{headerHeight}\" stroke=\"#cbd5e1\"/>");
        sb.Append(inv, $"<line x1=\"{leftWidth}\" y1=\"{TitleBandHeight}\" x2=\"{width}\" y2=\"{TitleBandHeight}\" stroke=\"#e2e8f0\"/>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Escape(string? value)
        => System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
}

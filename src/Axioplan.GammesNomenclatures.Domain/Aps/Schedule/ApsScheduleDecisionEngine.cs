using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;

namespace Axioplan.GammesNomenclatures.Domain.Aps.Schedule;

/// <summary>
/// Construit un planning exploitable (tâches OF/opérations, chemin critique, durées)
/// à partir des charges / saturations APS — sans second moteur de charge.
/// </summary>
public static class ApsScheduleDecisionEngine
{
    private static readonly string[] ResourceOrderHints =
    [
        "TRICOT", "REMAIL", "TRAIT", "EXPED", "EXT"
    ];

    public static ApsScheduleDecisionResult Build(
        DateOnly windowFrom,
        DateOnly windowTo,
        IReadOnlyList<ApsLoadBucketResult> loads,
        IReadOnlyList<ApsSaturationResult> saturations,
        ApsBottleneckResult bottleneck,
        DateOnly? needDate = null,
        DateOnly? today = null,
        string? demandId = null,
        string? finishedArticleCode = null,
        IReadOnlyList<ApsSegmentNeedLine>? cbnLines = null)
    {
        var todayDate = today ?? DateOnly.FromDateTime(DateTime.Today);
        var traces = new List<string>
        {
            "Planning dérivé des buckets de charge APS (pas d'estimation hors calcul)."
        };

        var internalLoads = loads
            .Where(l => !string.Equals(l.CapacityType, ApsResourceAlgebras.External, StringComparison.OrdinalIgnoreCase))
            .Where(l => l.QtyToLaunch > 0 || l.Charge > 0)
            .OrderBy(l => ResourceSortKey(l.ResourceCode))
            .ThenBy(l => l.BucketDate)
            .ThenBy(l => l.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var satByResource = saturations
            .GroupBy(s => s.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Rho ?? -1).First(), StringComparer.OrdinalIgnoreCase);

        var bottleneckCode = bottleneck.ConstrainingCode;
        var article = string.IsNullOrWhiteSpace(finishedArticleCode)
            ? cbnLines?.FirstOrDefault()?.ArticleCode ?? "PF"
            : finishedArticleCode;
        var orderId = string.IsNullOrWhiteSpace(demandId) ? $"PLAN-{windowFrom:yyyyMMdd}" : demandId;

        var tasks = new List<ApsScheduleTask>();
        string? ofId = null;

        if (internalLoads.Count > 0)
        {
            var ofStart = internalLoads.Min(l => l.BucketDate);
            var ofEnd = internalLoads.Max(l => l.BucketDate);
            ofId = $"OF-{Sanitize(orderId)}-{Sanitize(article)}";
            var ofQty = cbnLines?.Where(l => l.QtyToLaunch > 0).Sum(l => l.QtyToLaunch)
                        ?? internalLoads.Sum(l => l.QtyToLaunch);
            tasks.Add(new ApsScheduleTask(
                ofId,
                ApsScheduleTaskKinds.WorkOrder,
                $"OF {article}",
                null,
                bottleneckCode,
                article,
                ofStart,
                ofEnd,
                ofQty,
                internalLoads.Sum(l => l.Charge),
                ResolveStatus(ofStart, ofEnd, todayDate, needDate, GetSatStatus(satByResource, bottleneckCode)),
                IsBottleneck: true,
                IsOnCriticalPath: true,
                DependsOnTaskIds: [],
                OrderId: orderId));
            traces.Add($"OF {article} : {ofStart:yyyy-MM-dd} → {ofEnd:yyyy-MM-dd}.");
        }

        var opIdsByResource = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var prevResourceLastOp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? previousResource = null;
        string? previousResourceLastOpId = null;

        var seq = 0;
        foreach (var load in internalLoads)
        {
            seq++;
            var opId = $"OP-{Sanitize(load.ResourceCode)}-{load.BucketDate:yyyyMMdd}-{seq:D3}";
            var end = EstimateOperationEnd(load.BucketDate, load.Charge, load.UnitTime);
            var satStatus = GetSatStatus(satByResource, load.ResourceCode);
            var isBn = !string.IsNullOrWhiteSpace(bottleneckCode)
                       && load.ResourceCode.Equals(bottleneckCode, StringComparison.OrdinalIgnoreCase);

            var depends = new List<string>();
            if (ofId is not null)
            {
                depends.Add(ofId);
            }

            if (opIdsByResource.TryGetValue(load.ResourceCode, out var sameRes) && sameRes.Count > 0)
            {
                depends.Add(sameRes[^1]);
            }
            else if (previousResourceLastOpId is not null
                     && previousResource is not null
                     && !previousResource.Equals(load.ResourceCode, StringComparison.OrdinalIgnoreCase))
            {
                // Dépendance inter-ressources selon l'ordre de flux (tricot → remail → …).
                depends.Add(previousResourceLastOpId);
            }

            tasks.Add(new ApsScheduleTask(
                opId,
                ApsScheduleTaskKinds.Operation,
                $"{load.ResourceCode} · qté {load.QtyToLaunch:0.##}",
                ofId,
                load.ResourceCode,
                article,
                load.BucketDate,
                end,
                load.QtyToLaunch,
                load.Charge,
                ResolveStatus(load.BucketDate, end, todayDate, needDate, satStatus),
                isBn,
                IsOnCriticalPath: false,
                depends,
                orderId));

            if (!opIdsByResource.TryGetValue(load.ResourceCode, out var list))
            {
                list = [];
                opIdsByResource[load.ResourceCode] = list;
            }

            list.Add(opId);
            prevResourceLastOp[load.ResourceCode] = opId;

            if (previousResource is null
                || ResourceSortKey(load.ResourceCode) != ResourceSortKey(previousResource))
            {
                previousResource = load.ResourceCode;
            }

            previousResourceLastOpId = opId;
        }

        // Chemin critique : OF + opérations de la ressource goulot (ou toutes si goulot matériel).
        var criticalIds = new List<string>();
        if (ofId is not null)
        {
            criticalIds.Add(ofId);
        }

        var criticalOps = tasks
            .Where(t => t.Kind == ApsScheduleTaskKinds.Operation)
            .Where(t => t.IsBottleneck
                        || (string.IsNullOrWhiteSpace(bottleneckCode)
                            && satByResource.TryGetValue(t.ResourceCode ?? "", out var s)
                            && (s.Rho ?? 0) >= (saturations.Where(x => x.Rho.HasValue).Select(x => x.Rho!.Value).DefaultIfEmpty(0).Max())))
            .OrderBy(t => t.Start)
            .ThenBy(t => t.TaskId)
            .ToList();

        if (criticalOps.Count == 0)
        {
            criticalOps = tasks
                .Where(t => t.Kind == ApsScheduleTaskKinds.Operation)
                .OrderBy(t => t.Start)
                .ToList();
        }

        foreach (var op in criticalOps)
        {
            criticalIds.Add(op.TaskId);
        }

        tasks = tasks
            .Select(t => criticalIds.Contains(t.TaskId, StringComparer.OrdinalIgnoreCase)
                ? t with { IsOnCriticalPath = true }
                : t)
            .ToList();

        traces.Add(criticalIds.Count > 0
            ? $"Chemin critique : {string.Join(" → ", criticalIds)}."
            : "Chemin critique : aucune opération interne.");

        DateOnly start;
        DateOnly endDate;
        if (tasks.Count == 0)
        {
            start = windowFrom;
            endDate = windowFrom;
            traces.Add("Aucune charge interne — durée nulle sur la fenêtre.");
        }
        else
        {
            start = tasks.Min(t => t.Start);
            endDate = tasks.Max(t => t.End);
        }

        var workingDays = CountWorkingDays(start, endDate);
        var calendarDays = endDate.DayNumber - start.DayNumber + 1;
        int? margin = null;
        if (needDate is not null)
        {
            margin = CountWorkingDays(endDate.AddDays(1), needDate.Value);
            if (needDate.Value < endDate)
            {
                margin = -CountWorkingDays(needDate.Value.AddDays(1), endDate);
            }
            else if (needDate.Value == endDate)
            {
                margin = 0;
            }
        }

        var maxRho = saturations.Where(s => s.Rho.HasValue).Select(s => s.Rho!.Value).DefaultIfEmpty().Max();
        double? maxLoadRate = saturations.Any(s => s.Rho.HasValue) ? maxRho : null;

        traces.Add($"Durée : {workingDays} j ouvrés ({start:yyyy-MM-dd} → {endDate:yyyy-MM-dd}).");
        traces.Add($"Goulot : {bottleneckCode} — {bottleneck.Explanation}");

        var duration = new ApsScheduleDurationSummary(
            start,
            endDate,
            workingDays,
            calendarDays,
            margin,
            needDate,
            maxLoadRate,
            bottleneckCode,
            bottleneck.Explanation,
            criticalIds,
            traces);

        return new ApsScheduleDecisionResult(duration, tasks, todayDate, windowFrom, windowTo);
    }

    /// <summary>Fin d'opération : au moins le jour bucket ; allonge si charge importante (jours ouvrés).</summary>
    public static DateOnly EstimateOperationEnd(DateOnly start, double charge, double unitTime)
    {
        var extra = 0;
        if (charge > 8)
        {
            extra = (int)Math.Ceiling(charge / 8.0) - 1;
        }
        else if (unitTime > 0 && charge / Math.Max(unitTime, 0.0001) > 1 && charge > 4)
        {
            extra = 1;
        }

        var end = start;
        var added = 0;
        while (added < extra)
        {
            end = end.AddDays(1);
            if (end.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                added++;
            }
        }

        return end;
    }

    public static int CountWorkingDays(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return 0;
        }

        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                count++;
            }
        }

        return count;
    }

    public static string BuildTasksCsv(IReadOnlyList<ApsScheduleTask> tasks)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("TaskId;Kind;Label;ParentTaskId;Resource;Article;Start;End;Quantity;Charge;Status;IsBottleneck;IsCritical;DependsOn;OrderId");
        foreach (var t in tasks)
        {
            sb.Append(Escape(t.TaskId)).Append(';')
                .Append(Escape(t.Kind)).Append(';')
                .Append(Escape(t.Label)).Append(';')
                .Append(Escape(t.ParentTaskId)).Append(';')
                .Append(Escape(t.ResourceCode)).Append(';')
                .Append(Escape(t.ArticleCode)).Append(';')
                .Append(t.Start.ToString("yyyy-MM-dd")).Append(';')
                .Append(t.End.ToString("yyyy-MM-dd")).Append(';')
                .Append(t.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                .Append(t.Charge.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                .Append(Escape(t.Status)).Append(';')
                .Append(t.IsBottleneck ? "1" : "0").Append(';')
                .Append(t.IsOnCriticalPath ? "1" : "0").Append(';')
                .Append(Escape(string.Join("|", t.DependsOnTaskIds))).Append(';')
                .Append(Escape(t.OrderId))
                .AppendLine();
        }

        return sb.ToString();
    }

    private static string ResolveStatus(
        DateOnly start,
        DateOnly end,
        DateOnly today,
        DateOnly? needDate,
        string? saturationStatus)
    {
        if (saturationStatus is ApsSaturationStatuses.Depassement or ApsSaturationStatuses.Saturee
            or ApsSaturationStatuses.SaturationInfinie)
        {
            return ApsScheduleTaskStatuses.Saturated;
        }

        if (needDate is not null && end > needDate.Value)
        {
            return ApsScheduleTaskStatuses.Late;
        }

        if (end < today)
        {
            return ApsScheduleTaskStatuses.Done;
        }

        if (start <= today && end >= today)
        {
            return ApsScheduleTaskStatuses.InProgress;
        }

        return ApsScheduleTaskStatuses.Planned;
    }

    private static string? GetSatStatus(
        IReadOnlyDictionary<string, ApsSaturationResult> satByResource,
        string? resourceCode)
    {
        if (string.IsNullOrWhiteSpace(resourceCode))
        {
            return null;
        }

        return satByResource.TryGetValue(resourceCode, out var s) ? s.Status : null;
    }

    private static int ResourceSortKey(string resourceCode)
    {
        for (var i = 0; i < ResourceOrderHints.Length; i++)
        {
            if (resourceCode.Contains(ResourceOrderHints[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 50;
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray();
        return chars.Length == 0 ? "X" : new string(chars);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}

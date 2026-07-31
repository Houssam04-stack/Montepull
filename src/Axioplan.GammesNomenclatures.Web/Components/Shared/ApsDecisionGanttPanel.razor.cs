using System.Globalization;
using System.Net;
using Axioplan.GammesNomenclatures.Application.Aps;
using Axioplan.GammesNomenclatures.Domain.Aps.Schedule;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Axioplan.GammesNomenclatures.Web.Components.Shared;

public partial class ApsDecisionGanttPanel
{
    [Inject] private ApsScheduleDecisionService DecisionService { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Parameter] public ApsScheduleDecisionResult? Schedule { get; set; }
    [Parameter] public string PanelTitle { get; set; } = "Planification";
    [Parameter] public string GanttSvgId { get; set; } = "aps-gantt-svg";

    private ApsGanttLayoutResult? _layout;
    private bool _exportBusy;
    private string? _exportInfo;
    private string _printRootId = "aps-gantt-print-root";
    private string? _articleToken;

    private string LeftWidthPx =>
        (_layout?.LeftPanelWidth ?? ApsGanttLayoutEngine.LeftPanelWidthPx)
        .ToString(CultureInfo.InvariantCulture) + "px";

    private string HeaderHeightPx =>
        (_layout?.HeaderHeight ?? 64).ToString(CultureInfo.InvariantCulture) + "px";

    private string TimelineWidthPx =>
        (_layout?.TimelineWidth ?? 800).ToString(CultureInfo.InvariantCulture) + "px";

    protected override void OnParametersSet()
    {
        _printRootId = $"aps-gantt-print-{GanttSvgId}";
        if (Schedule is null || Schedule.Tasks.Count == 0)
        {
            _layout = null;
            return;
        }

        _articleToken = Schedule.Tasks
            .FirstOrDefault(t => t.Kind == ApsScheduleTaskKinds.WorkOrder)?.ArticleCode
            ?? Schedule.Duration.BottleneckResourceCode
            ?? PanelTitle;

        _layout = ApsGanttLayoutEngine.Compute(Schedule, $"Gantt APS - {PanelTitle}", GanttSvgId);
    }

    private static string MarginClass(ApsScheduleDurationSummary d)
        => d.MarginWorkingDays.HasValue && d.MarginWorkingDays.Value < 0
            ? "aps-kpi aps-kpi-danger"
            : "aps-kpi";

    private static string RowClass(ApsGanttRowView row)
    {
        var css = "gantt-left-row level-" + (int)row.Level;
        if (row.IsOf)
        {
            css += " is-of";
        }

        if (row.IsCritical)
        {
            css += " is-critical";
        }

        if (row.IsBottleneck)
        {
            css += " is-bottleneck";
        }

        return css;
    }

    private static string NameClass(ApsGanttRowView row)
        => row.Level switch
        {
            ApsGanttRowLevel.WorkOrder => "gantt-name gantt-name-of",
            ApsGanttRowLevel.Resource => "gantt-name gantt-name-res",
            _ => "gantt-name gantt-name-op"
        };

    private static string StatusClass(ApsGanttRowView row)
        => $"gantt-status gantt-status-{row.StatusCss}";

    private string FileBase(string kind, string ext)
        => ApsGanttLayoutEngine.BuildExportFileName(kind, _articleToken, DateOnly.FromDateTime(DateTime.Today), ext);

    private async Task ExportCsvAsync()
    {
        if (Schedule is null) return;
        _exportBusy = true;
        try
        {
            var csv = DecisionService.BuildTasksCsv(Schedule);
            await Js.InvokeVoidAsync("axioplan.downloadText", FileBase("csv", "csv"), csv, "text/csv;charset=utf-8");
            _exportInfo = "CSV téléchargé dans le dossier Téléchargements du navigateur.";
        }
        catch (Exception ex) { _exportInfo = ex.Message; }
        finally { _exportBusy = false; }
    }

    private async Task ExportExcelAsync()
    {
        if (Schedule is null) return;
        _exportBusy = true;
        try
        {
            var xml = DecisionService.BuildExcelSpreadsheetMl(Schedule);
            await Js.InvokeVoidAsync("axioplan.downloadText", FileBase("xlsx", "xls"), xml, "application/vnd.ms-excel");
            _exportInfo = "Excel téléchargé dans le dossier Téléchargements du navigateur.";
        }
        catch (Exception ex) { _exportInfo = ex.Message; }
        finally { _exportBusy = false; }
    }

    private async Task ExportSvgAsync()
    {
        if (_layout is null) return;
        _exportBusy = true;
        try
        {
            await Js.InvokeVoidAsync("axioplan.downloadSvgMarkup", _layout.FullSvgMarkup, FileBase("svg", "svg"));
            _exportInfo = "SVG complet téléchargé (Téléchargements du navigateur).";
        }
        catch (Exception ex) { _exportInfo = ex.Message; }
        finally { _exportBusy = false; }
    }

    private async Task ExportPngAsync()
    {
        if (_layout is null) return;
        _exportBusy = true;
        try
        {
            var ok = await Js.InvokeAsync<bool>(
                "axioplan.downloadPngFromSvgMarkup",
                _layout.FullSvgMarkup,
                FileBase("png", "png"),
                2.0);
            _exportInfo = ok
                ? "PNG haute résolution téléchargé (Téléchargements du navigateur)."
                : "Échec export PNG.";
        }
        catch (Exception ex) { _exportInfo = ex.Message; }
        finally { _exportBusy = false; }
    }

    private async Task ExportPdfAsync()
    {
        if (Schedule is null || _layout is null) return;
        _exportBusy = true;
        try
        {
            var d = Schedule.Duration;
            var summary =
                "<div class=\"kpis gantt-summary\">" +
                Kpi("Durée estimée", $"{d.WorkingDays} jours ouvrés") +
                Kpi("Date de début", d.StartDate.ToString("dd/MM/yyyy")) +
                Kpi("Date de fin estimée", d.EstimatedEndDate.ToString("dd/MM/yyyy")) +
                Kpi("Marge restante", $"{(d.MarginWorkingDays?.ToString() ?? "-")} jours ouvrés") +
                Kpi("Date de besoin", d.NeedDate?.ToString("dd/MM/yyyy") ?? "-") +
                Kpi("Charge max (rho)", d.MaxLoadRate?.ToString("P1") ?? "-") +
                Kpi("Ressource goulot", WebUtility.HtmlEncode(d.BottleneckResourceCode ?? "-")) +
                "</div>" +
                _layout.TasksTableHtml +
                "<div class=\"gantt-critical-path\"><strong>Chemin critique</strong><p>" +
                WebUtility.HtmlEncode(string.Join(" → ", _layout.CriticalPathLabels)) +
                "</p></div>" +
                "<div class=\"aps-gantt-legend\"><span>Planifié</span><span>En cours</span><span>Retard</span>" +
                "<span>Saturé</span><span>Terminé</span><span>Chemin critique</span><span>Goulot</span></div>";

            await Js.InvokeVoidAsync(
                "axioplan.printGanttPdfDocument",
                $"Planning APS - {PanelTitle}",
                DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                summary,
                _layout.FullSvgMarkup,
                FileBase("pdf", "pdf"));
            _exportInfo = "Document d'impression ouvert — Enregistrer au format PDF (paysage A4). Fichier suggéré : "
                          + FileBase("pdf", "pdf");
        }
        catch (Exception ex) { _exportInfo = ex.Message; }
        finally { _exportBusy = false; }
    }

    private static string Kpi(string label, string value)
        => $"<div class=\"kpi\"><span>{label}</span><strong>{value}</strong></div>";
}

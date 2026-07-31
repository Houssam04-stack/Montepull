using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Schedule;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Aps;

public static class ApsScheduleDecisionExtensions
{
    public static IServiceCollection AddApsScheduleDecisionApplication(this IServiceCollection services)
    {
        services.AddScoped<ApsScheduleDecisionService>();
        return services;
    }
}

/// <summary>
/// Aide à la décision planificateur : durées, chemin critique, tâches Gantt/exports
/// régénérés depuis chaque résultat APS (pas d'estimation hors planning).
/// </summary>
public sealed class ApsScheduleDecisionService
{
    public ApsScheduleDecisionResult BuildFromFlux(
        ApsFluxComputeResultDto flux,
        DateOnly? needDate = null,
        string? demandId = null,
        string? finishedArticleCode = null,
        IReadOnlyList<ApsSegmentNeedLine>? cbnLines = null,
        DateOnly? today = null)
        => ApsScheduleDecisionEngine.Build(
            flux.From,
            flux.To,
            flux.Loads,
            flux.Saturations,
            flux.Bottleneck,
            needDate,
            today,
            demandId,
            finishedArticleCode,
            cbnLines);

    public ApsScheduleDecisionResult BuildFromPlanning(
        ApsPlanningResultDto planning,
        DateOnly? needDate = null,
        DateOnly? today = null)
        => BuildFromFlux(
            planning.Flux,
            needDate,
            planning.CbnResult?.DemandId,
            planning.CbnResult?.FinishedArticleCode,
            planning.CbnResult?.Lines,
            today);

    public string BuildTasksCsv(ApsScheduleDecisionResult decision)
        => ApsScheduleDecisionEngine.BuildTasksCsv(decision.Tasks);

    public string BuildExcelSpreadsheetMl(ApsScheduleDecisionResult decision)
    {
        var rows = new System.Text.StringBuilder();
        rows.AppendLine("<Row>");
        foreach (var h in new[]
                 {
                     "TaskId", "Kind", "Label", "Parent", "Resource", "Article", "Start", "End",
                     "Quantity", "Charge", "Status", "Bottleneck", "Critical", "DependsOn", "OrderId"
                 })
        {
            rows.Append("<Cell><Data ss:Type=\"String\">").Append(Xml(h)).Append("</Data></Cell>");
        }

        rows.AppendLine("</Row>");

        foreach (var t in decision.Tasks)
        {
            rows.Append("<Row>");
            AppendCell(rows, t.TaskId);
            AppendCell(rows, t.Kind);
            AppendCell(rows, t.Label);
            AppendCell(rows, t.ParentTaskId);
            AppendCell(rows, t.ResourceCode);
            AppendCell(rows, t.ArticleCode);
            AppendCell(rows, t.Start.ToString("yyyy-MM-dd"));
            AppendCell(rows, t.End.ToString("yyyy-MM-dd"));
            AppendNumber(rows, t.Quantity);
            AppendNumber(rows, t.Charge);
            AppendCell(rows, t.Status);
            AppendCell(rows, t.IsBottleneck ? "1" : "0");
            AppendCell(rows, t.IsOnCriticalPath ? "1" : "0");
            AppendCell(rows, string.Join("|", t.DependsOnTaskIds));
            AppendCell(rows, t.OrderId);
            rows.AppendLine("</Row>");
        }

        return $"""
            <?xml version="1.0"?>
            <?mso-application progid="Excel.Sheet"?>
            <Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"
             xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
             <Worksheet ss:Name="Planning">
              <Table>
            {rows}
              </Table>
             </Worksheet>
             <Worksheet ss:Name="Synthese">
              <Table>
               <Row><Cell><Data ss:Type="String">Debut</Data></Cell><Cell><Data ss:Type="String">{Xml(decision.Duration.StartDate.ToString("yyyy-MM-dd"))}</Data></Cell></Row>
               <Row><Cell><Data ss:Type="String">Fin estimee</Data></Cell><Cell><Data ss:Type="String">{Xml(decision.Duration.EstimatedEndDate.ToString("yyyy-MM-dd"))}</Data></Cell></Row>
               <Row><Cell><Data ss:Type="String">Jours ouvres</Data></Cell><Cell><Data ss:Type="Number">{decision.Duration.WorkingDays}</Data></Cell></Row>
               <Row><Cell><Data ss:Type="String">Marge jours ouvres</Data></Cell><Cell><Data ss:Type="Number">{decision.Duration.MarginWorkingDays ?? 0}</Data></Cell></Row>
               <Row><Cell><Data ss:Type="String">Rho max</Data></Cell><Cell><Data ss:Type="Number">{(decision.Duration.MaxLoadRate ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)}</Data></Cell></Row>
               <Row><Cell><Data ss:Type="String">Goulot</Data></Cell><Cell><Data ss:Type="String">{Xml(decision.Duration.BottleneckResourceCode)}</Data></Cell></Row>
              </Table>
             </Worksheet>
            </Workbook>
            """;
    }

    private static void AppendCell(System.Text.StringBuilder sb, string? value)
        => sb.Append("<Cell><Data ss:Type=\"String\">").Append(Xml(value)).Append("</Data></Cell>");

    private static void AppendNumber(System.Text.StringBuilder sb, double value)
        => sb.Append("<Cell><Data ss:Type=\"Number\">")
            .Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append("</Data></Cell>");

    private static string Xml(string? value)
        => System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
}

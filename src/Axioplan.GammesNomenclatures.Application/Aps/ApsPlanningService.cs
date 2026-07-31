using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.MontepullImport;
using Axioplan.GammesNomenclatures.Application.Mvp0;
using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Aps;

public static class ApsPlanningExtensions
{
    public static IServiceCollection AddApsPlanningApplication(this IServiceCollection services)
    {
        services.AddScoped<ApsPlanningService>();
        return services;
    }
}

/// <summary>
/// Orchestrateur planification APS : CBN segment → capacites nettes → charges / saturation.
/// </summary>
public sealed class ApsPlanningService(
    ApsSegmentCbnService cbnService,
    ApsCapacityService capacityService,
    ApsCapacityEvaluator capacityEvaluator,
    ApsFluxService fluxService,
    IApsReferentialRepository referentialRepository,
    Mvp0ApsGateGuard gateGuard,
    Mvp0WorkflowService mvp0Workflow,
    PlanningDatasetProvider dataset)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await mvp0Workflow.EnsureReadyAsync(cancellationToken);
        await referentialRepository.EnsureSchemaAsync(cancellationToken);
        if (!await dataset.IsMontepullRealAsync(cancellationToken))
        {
            await referentialRepository.SeedDemoMinimalAsync(cancellationToken);
        }

        await cbnService.EnsureReadyAsync(cancellationToken);
        await capacityService.EnsureReadyAsync(cancellationToken);
        await fluxService.EnsureReadyAsync(cancellationToken);
    }

    public async Task<ApsPlanningResultDto> RunAsync(
        ApsPlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        var isReal = await dataset.IsMontepullRealAsync(cancellationToken);
        var effectiveSource = isReal ? CalculationSourceType.Real : request.SourceType;
        var approvedCampaign = await gateGuard.RequireApprovedCampaignAsync(
            effectiveSource,
            request.Mvp0CampaignId,
            cancellationToken);

        await EnsureReadyAsync(cancellationToken);
        var traces = new List<string>
        {
            $"Dataset : {await dataset.GetActiveAsync(cancellationToken)}",
            $"Planification APS {request.From:yyyy-MM-dd} → {request.To:yyyy-MM-dd}",
            effectiveSource == CalculationSourceType.Demo
                ? "Mode DEMO — gate MVP-0 contourné (données seeds)."
                : $"Gate 0→1 : campagne {approvedCampaign.Code} ({approvedCampaign.GateOutcome}) — famille {approvedCampaign.FamilyCode}."
        };

        long? cbnRunId = request.SegmentCbnRunId;
        ApsSegmentCbnResult? cbnResult = null;

        if (request.CbnRequest is not null)
        {
            if (request.RunSegmentCascade)
            {
                var cascade = await cbnService.RunCascadeAsync(request.CbnRequest, cancellationToken);
                cbnRunId = cascade.LastRunId;
                cbnResult = cascade.LastResult;
                foreach (var run in cascade.SegmentRuns)
                {
                    traces.Add(run.RunId is long id
                        ? $"CBN {run.Result.Segment} #{id} — {run.Result.Lines.Count(l => l.QtyToLaunch > 0)} lancements."
                        : $"CBN {run.Result.Segment} — artefact {run.ArtifactStatus}.");
                }
            }
            else
            {
                var cbnRun = await cbnService.RunAsync(request.CbnRequest, cancellationToken);
                cbnResult = cbnRun.Result;
                cbnRunId = cbnRun.RunId;
                traces.Add(cbnRun.RunId is long id
                    ? $"CBN segment #{id} — {cbnRun.Result.Lines.Count(l => l.QtyToLaunch > 0)} lancements."
                    : "CBN segment execute (id non persiste).");
            }
        }
        else if (cbnRunId is long runId)
        {
            traces.Add($"CBN segment existant #{runId} reutilise.");
        }
        else if (isReal)
        {
            traces.Add("Pas de CBN segment — lancements MONTEPULL_REAL via OF/commandes restantes.");
        }
        else
        {
            traces.Add("Pas de CBN — lancements demo utilises pour les charges.");
        }

        var resources = await capacityService.ListResourcesAsync(cancellationToken);
        var capacities = new List<ApsResourceCapacitySummaryDto>();
        foreach (var resource in resources)
        {
            var summary = await capacityEvaluator.EvaluateResourceAsync(
                resource,
                request.From,
                request.To,
                cancellationToken: cancellationToken);
            capacities.Add(summary);
            traces.Add($"Capacite {resource.Code}: Cap_cum = {summary.CapCumEngageable:F2} {resource.Unit}");
        }

        var flux = await fluxService.ComputeAsync(
            new ApsFluxWindowRequest(
                request.From,
                request.To,
                cbnRunId,
                request.PublishFlux,
                request.Thresholds),
            cancellationToken);

        traces.AddRange(flux.Traces);
        traces.Add($"Goulot: {flux.Bottleneck.ConstrainingCode} — {flux.Bottleneck.Explanation}");

        return new ApsPlanningResultDto(
            request.From,
            request.To,
            cbnRunId,
            cbnResult,
            capacities,
            flux,
            traces,
            approvedCampaign.Id == Guid.Empty ? null : approvedCampaign.Id,
            approvedCampaign.GateOutcome);
    }
}

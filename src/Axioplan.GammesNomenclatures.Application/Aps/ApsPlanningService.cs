using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.Abstractions;
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
    IApsReferentialRepository referentialRepository)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await referentialRepository.EnsureSchemaAsync(cancellationToken);
        await referentialRepository.SeedDemoMinimalAsync(cancellationToken);
        await cbnService.EnsureReadyAsync(cancellationToken);
        await capacityService.EnsureReadyAsync(cancellationToken);
        await fluxService.EnsureReadyAsync(cancellationToken);
    }

    public async Task<ApsPlanningResultDto> RunAsync(
        ApsPlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var traces = new List<string>
        {
            $"Planification APS {request.From:yyyy-MM-dd} → {request.To:yyyy-MM-dd}"
        };

        long? cbnRunId = request.SegmentCbnRunId;
        ApsSegmentCbnResult? cbnResult = null;

        if (request.CbnRequest is not null)
        {
            var cbnRun = await cbnService.RunAsync(request.CbnRequest, cancellationToken);
            cbnResult = cbnRun.Result;
            cbnRunId = cbnRun.RunId;
            traces.Add(cbnRun.RunId is long id
                ? $"CBN segment #{id} — {cbnRun.Result.Lines.Count(l => l.QtyToLaunch > 0)} lancements."
                : "CBN segment execute (id non persiste).");
        }
        else if (cbnRunId is long runId)
        {
            traces.Add($"CBN segment existant #{runId} reutilise.");
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
            traces);
    }
}

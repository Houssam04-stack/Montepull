using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Aps;

public static class ApsFluxExtensions
{
    public static IServiceCollection AddApsFluxApplication(this IServiceCollection services)
    {
        services.AddScoped<ApsFluxService>();
        return services;
    }
}

public sealed class ApsFluxService(
    IApsFluxRepository fluxRepository,
    IApsCapacityRepository capacityRepository,
    ApsCapacityEvaluator capacityEvaluator,
    IApsJournalRepository journalRepository)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await fluxRepository.EnsureSchemaAsync(cancellationToken);
        await capacityRepository.EnsureSchemaAsync(cancellationToken);
        await fluxRepository.SeedDemoAsync(cancellationToken);
        await capacityRepository.SeedDemoAsync(cancellationToken);
    }

    public Task<IReadOnlyList<(DateTime At, string Code, string Kind, string Explanation)>> ListBottleneckHistoryAsync(
        CancellationToken cancellationToken = default)
        => fluxRepository.ListBottleneckHistoryAsync(20, cancellationToken);

    public Task<IReadOnlyList<ApsLoadRunSummaryDto>> ListLoadRunsAsync(CancellationToken cancellationToken = default)
        => fluxRepository.ListLoadRunsAsync(20, cancellationToken);

    public async Task<ApsFluxComputeResultDto> ComputeAsync(
        ApsFluxWindowRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var traces = new List<string>
        {
            $"Fenetre {request.From:yyyy-MM-dd} → {request.To:yyyy-MM-dd}",
            request.Publish ? "Mode PUBLIE (journalisation active)." : "Mode calcul temporaire (pas de journal sauf publication)."
        };

        var launches = await fluxRepository.ResolveLaunchesFromCbnRunAsync(
            request.SegmentCbnRunId, request.From, request.To, cancellationToken);
        if (launches.Count == 0)
        {
            // Demo launches SIMULES isolaris via resources
            launches = BuildDemoLaunches(request.From);
            traces.Add("Aucun run CBN APS — lancements demo SIMULES utilises.");
        }

        var resources = await capacityRepository.ListResourcesAsync(cancellationToken);
        var bucketLoads = new List<ApsLoadBucketResult>();

        foreach (var launch in launches)
        {
            var resource = resources.FirstOrDefault(r =>
                r.Code.Equals(launch.ResourceCode, StringComparison.OrdinalIgnoreCase));
            if (resource is null)
            {
                traces.Add($"Ressource {launch.ResourceCode} inconnue — ignoree.");
                continue;
            }

            var (valid, hash, loadLines) = await fluxRepository.LoadValidChargesAsync(
                launch.ArticleCode,
                GuessSegment(launch.ResourceCode),
                GuessCircuit(launch.ResourceCode),
                cancellationToken);

            ApsCompiledLoadLine line;
            if (resource.ResourceType == "LOT")
            {
                line = new ApsCompiledLoadLine(
                    resource.Code, ApsResourceAlgebras.Lot, 0, 0, resource.Unit, true, hash.Length > 0 ? hash : "demo-lot");
            }
            else if (!valid || loadLines.Count == 0)
            {
                throw new InvalidOperationException(
                    $"CompileInvalideRefuse : artefact CHARGES INVALID/absent pour {launch.ArticleCode}/{launch.ResourceCode}.");
            }
            else
            {
                line = loadLines.FirstOrDefault(l =>
                           l.ResourceCode.Equals(launch.ResourceCode, StringComparison.OrdinalIgnoreCase))
                       ?? loadLines[0] with { ResourceCode = launch.ResourceCode, Unit = resource.Unit, CapacityType = resource.ResourceType };
            }

            var bathMax = resource.ResourceType == "LOT"
                ? await capacityEvaluator.GetBathMaxFillAsync(resource.Code, cancellationToken) ?? 40.0
                : (double?)null;
            bucketLoads.Add(LoadEngine.ComputeBucket(
                line with { ArtifactValid = true },
                launch,
                resource.ResourceType,
                resource.Unit,
                bathMax));
        }

        // EXTERNE demo without internal load
        var extLaunch = new ApsLaunchQuantity("DEMO_PF", "EXT_ST_REMAIL", request.From, 50);
        var extLine = new ApsCompiledLoadLine("EXT_ST_REMAIL", ApsResourceAlgebras.External, 0, 0, "NONE", true, "demo-ext");
        bucketLoads.Add(LoadEngine.ComputeBucket(extLine, extLaunch, ApsResourceAlgebras.External, "NONE"));

        var saturations = new List<ApsSaturationResult>();
        foreach (var group in bucketLoads.Where(b => b.CapacityType != ApsResourceAlgebras.External)
                     .GroupBy(b => b.ResourceCode, StringComparer.OrdinalIgnoreCase))
        {
            var resource = resources.First(r => r.Code.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            var chargeCum = group.Where(b => b.BucketDate >= request.From && b.BucketDate <= request.To).Sum(b => b.Charge);

            // Cap_cum engageable via moteur capacite
            var capEval = await capacityRepository.GetResourceAsync(resource.Code, cancellationToken);
            double capCum = 0;
            if (capEval is not null)
            {
                capCum = await capacityEvaluator.EvaluateCapCumEngageableAsync(
                    resource.Code, request.From, request.To, cancellationToken);
            }

            saturations.Add(SaturationEngine.Compute(
                resource.Code,
                request.From,
                request.To,
                chargeCum,
                capCum,
                resource.RhoTarget,
                request.Thresholds));
        }

        // Material FIL constraint if shortage from demo stocks
        ApsMaterialConstraint? yarn = null;
        var yarnShortage = launches
            .Where(l => l.ResourceCode.Contains("TRICOT", StringComparison.OrdinalIgnoreCase))
            .Sum(l => l.QtyToLaunch * 0.2); // TO_CONFIRM ratio
        // If any saturation exists and yarn shortage signal from buffer FIL-DEMO demo path
        var yarnBlocking = request.SegmentCbnRunId is null && yarnShortage > 200; // rare
        // Explicit path: if user window includes forced FIL — check space of FIL shortage via launches > available demo 40
        if (launches.Any(l => l.QtyToLaunch > 200 && l.ResourceCode.Contains("TRICOT", StringComparison.OrdinalIgnoreCase)))
        {
            yarn = new ApsMaterialConstraint("FIL", true, yarnShortage - 40, "ATP fil insuffisant (SIMULE) avant charge machine.");
        }

        var bottleneck = BottleneckEngine.Identify(request.From, request.To, saturations, yarn);
        var previous = await fluxRepository.GetLastBottleneckAsync(request.From, request.To, cancellationToken);
        var windowsComparable = previous is not null
                                && previous.From == request.From
                                && previous.To == request.To;
        var moved = BottleneckEngine.ShouldJournalBottleneckMoved(previous, bottleneck, windowsComparable);

        var remailCapDay = await capacityEvaluator.EvaluateCapCumEngageableAsync(
            "CC_REMAILLAGE", request.From, request.From, cancellationToken);
        var bufferStock = await fluxRepository.LoadUpstreamBufferStockAsync("CC_REMAILLAGE", cancellationToken);
        var expectedMix = await fluxRepository.LoadExpectedMixAsync(cancellationToken);
        var buffer = BufferEngine.ComputeUpstreamBuffer(
            "CC_REMAILLAGE",
            bufferStock,
            remailCapDay <= 0 ? 6.0 : remailCapDay, // demo fallback TO_CONFIRM
            expectedMix.Count == 0 ? null : expectedMix);

        var (k, variances, reaction) = await fluxRepository.LoadBufferTargetParamsAsync("CC_REMAILLAGE", cancellationToken);
        var bufferTarget = BufferEngine.ComputeTarget(buffer.BufferHre, k, variances, reaction);

        var spaces = new List<ApsSpaceSaturationResult>();
        foreach (var (place, occupied, max) in await fluxRepository.LoadSpaceCapacitiesAsync(cancellationToken))
        {
            spaces.Add(SpaceSaturationEngine.Compute(place, occupied, max));
        }

        var spaceAlert = spaces.Any(s => s.LimitReached);
        var plannedRemail = saturations
            .Where(s => s.ResourceCode.Equals("CC_REMAILLAGE", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.ChargeCumulee)
            .FirstOrDefault();
        var targetHre = bufferTarget.BufferTargetHre ?? buffer.BufferHre; // if TO_CONFIRM use actual for demo formula visibility
        var rope = RopeEngine.RecommendKnittingLaunch(
            plannedRemail,
            bufferTarget.BufferTargetHre ?? buffer.BufferHre,
            buffer.BufferHre,
            buffer.Composition
                .GroupBy(c => c.ArticleCode)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity), StringComparer.OrdinalIgnoreCase),
            spaceAlert);

        traces.Add(bottleneck.Explanation);
        traces.AddRange(buffer.Traces);
        traces.Add(bufferTarget.Cause);
        traces.AddRange(rope.Traces);
        traces.AddRange(spaces.Select(s => s.Trace));
        traces.AddRange(saturations.Select(s => $"CAPACITE {s.ResourceCode}: {s.Cause}"));

        var dto = new ApsFluxComputeResultDto(
            request.From,
            request.To,
            bucketLoads,
            saturations,
            bottleneck,
            false,
            buffer,
            bufferTarget,
            rope,
            spaces,
            null,
            traces);

        if (!request.Publish)
        {
            return dto;
        }

        // Publication : journal + persist
        await journalRepository.EnsureSchemaAsync(cancellationToken);
        await fluxRepository.SavePublishedRunAsync(dto, cancellationToken);
        await fluxRepository.PersistBottleneckHistoryAsync(bottleneck, moved, cancellationToken);

        if (moved)
        {
            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.BottleneckMoved,
                DateTime.UtcNow,
                $"bn-{request.From:yyyyMMdd}-{request.To:yyyyMMdd}",
                JsonSerializer.Serialize(new { previous = previous?.ConstrainingCode, current = bottleneck.ConstrainingCode, bottleneck.Explanation }),
                "aps-flux"), cancellationToken);
        }

        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.BufferMeasured,
            DateTime.UtcNow,
            "buffer-CC_REMAILLAGE",
            JsonSerializer.Serialize(new { buffer.BufferHre, buffer.CoverageDays, buffer.AdequacyStatus }),
            "aps-flux"), cancellationToken);

        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.RopeRecommendationPublished,
            DateTime.UtcNow,
            $"rope-{DateTime.UtcNow:yyyyMMddHHmmss}",
            JsonSerializer.Serialize(new { rope.RecommendedQty, rope.Priority, rope.CreatesWorkOrder }),
            "aps-flux"), cancellationToken);

        foreach (var sat in saturations.Where(s =>
                     s.Status is ApsSaturationStatuses.Depassement or ApsSaturationStatuses.Saturee
                     && s.Rho > s.RhoTarget))
        {
            // options epuisees : demo si palier ST non activable (horizon court)
            var horizonDays = request.To.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
            if (horizonDays < 45)
            {
                await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                    ApsFactEventTypes.EnvelopeViolated,
                    DateTime.UtcNow,
                    sat.ResourceCode,
                    JsonSerializer.Serialize(new { sat.Rho, sat.RhoTarget, sat.Status, note = "Options (ST) non activables sur horizon" }),
                    "aps-flux"), cancellationToken);
            }
        }

        foreach (var sp in spaces.Where(s => s.LimitReached))
        {
            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.StockZoneSaturated,
                DateTime.UtcNow,
                sp.PlaceCode,
                JsonSerializer.Serialize(sp),
                "aps-flux"), cancellationToken);
        }

        return dto with { BottleneckMovedJournaled = moved };
    }

    private static IReadOnlyList<ApsLaunchQuantity> BuildDemoLaunches(DateOnly from)
        =>
        [
            new("DEMO_PF", "CC_REMAILLAGE", from, 100),
            new("DEMO_PF", "CC_TRICOTAGE", from, 80),
            new("DEMO_PF", "CC_TRAITEMENT", from, 120)
        ];

    private static string GuessSegment(string resourceCode)
        => resourceCode.Contains("TRICOT", StringComparison.OrdinalIgnoreCase) ? "SEG_A"
            : resourceCode.Contains("REMAIL", StringComparison.OrdinalIgnoreCase) ? "SEG_B"
            : "SEG_C";

    private static string GuessCircuit(string resourceCode)
        => resourceCode.Contains("TRICOT", StringComparison.OrdinalIgnoreCase) ? "CIR_TRICOT_INT"
            : resourceCode.Contains("REMAIL", StringComparison.OrdinalIgnoreCase) ? "CIR_REMAIL_INT"
            : "CIR_EXPED";
}

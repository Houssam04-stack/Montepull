using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Capacity;
using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Aps;

public static class ApsCapacityCbnExtensions
{
    public static IServiceCollection AddApsCapacityCbnApplication(this IServiceCollection services)
    {
        services.AddScoped<ApsCapacityEvaluator>();
        services.AddScoped<ApsCapacityService>();
        services.AddScoped<ApsSegmentCbnService>();
        return services;
    }
}

public sealed class ApsCapacityService(
    IApsCapacityRepository capacityRepository,
    IApsJournalRepository journalRepository,
    ApsCapacityEvaluator capacityEvaluator)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await capacityRepository.EnsureSchemaAsync(cancellationToken);
        await capacityRepository.SeedDemoAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ApsCapacityResourceDto>> ListResourcesAsync(CancellationToken cancellationToken = default)
        => capacityRepository.ListResourcesAsync(cancellationToken);

    public async Task<ApsCapacityWindowResultDto> EvaluateWindowAsync(
        ApsCapacityWindowRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var resource = await capacityRepository.GetResourceAsync(request.ResourceCode, cancellationToken)
                       ?? throw new InvalidOperationException($"Ressource '{request.ResourceCode}' introuvable.");

        var summary = await capacityEvaluator.EvaluateResourceAsync(
            resource,
            request.From,
            request.To,
            request.FamilyCode,
            request.TeamCode,
            cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var capCum = NetCapacityEngine.ComputeCapCum(
            resource.Code,
            resource.Unit,
            request.From,
            request.To,
            today,
            summary.Buckets,
            await capacityRepository.GetTiersAsync(request.ResourceCode, cancellationToken));

        var engagements = await capacityRepository.GetExternalEngagementsAsync("SEG_B", cancellationToken);
        var external = NetCapacityEngine.ComputeExternalAvailability(
            engagements,
            "SEG_B",
            today,
            request.To);
        var externalNote = external <= 0
            ? "Aucun engagement compatible — capacite externe = 0 (CDC)."
            : $"Volume residuel engagements OPEN compatibles = {external:F2}";

        var reservations = await capacityRepository.GetReservationsAsync(
            request.ResourceCode, request.From, request.To, cancellationToken);

        return new ApsCapacityWindowResultDto(
            resource,
            summary.Buckets,
            capCum,
            external,
            externalNote,
            reservations,
            capCum.Tiers);
    }

    public async Task ReserveDemoAsync(
        string resourceCode,
        DateOnly bucketDate,
        double qty,
        string aggregateId,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var resource = await capacityRepository.GetResourceAsync(resourceCode, cancellationToken)
                       ?? throw new InvalidOperationException("Ressource introuvable.");
        var reservation = new ApsCapacityReservation(resourceCode, bucketDate, qty, resource.Unit, aggregateId, "demo");
        await capacityRepository.ReserveAsync(reservation, cancellationToken);
        await journalRepository.EnsureSchemaAsync(cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.CapacityReserved,
            DateTime.UtcNow,
            aggregateId,
            JsonSerializer.Serialize(reservation),
            "aps-capacity"), cancellationToken);
    }

    public async Task ReleaseDemoAsync(
        string resourceCode,
        DateOnly bucketDate,
        double qty,
        string aggregateId,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await capacityRepository.ReleaseAsync(resourceCode, bucketDate, aggregateId, qty, cancellationToken);
        await journalRepository.EnsureSchemaAsync(cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.CapacityReleased,
            DateTime.UtcNow,
            aggregateId,
            JsonSerializer.Serialize(new { resourceCode, bucketDate, qty }),
            "aps-capacity"), cancellationToken);
    }
}

public sealed class ApsSegmentCbnService(
    IApsSegmentCbnRepository cbnRepository,
    IApsJournalRepository journalRepository)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await cbnRepository.EnsureSchemaAsync(cancellationToken);
        await cbnRepository.SeedDemoAsync(cancellationToken);
    }

    public async Task<ApsSegmentCbnRunDto> RunAsync(
        ApsSegmentCbnRunRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var (valid, hash, needs) = await cbnRepository.LoadValidNeedsAsync(
            request.FinishedArticleCode,
            request.Segment,
            request.CircuitChain,
            cancellationToken);

        if (!valid)
        {
            await journalRepository.EnsureSchemaAsync(cancellationToken);
            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.CompiledInvalidated,
                DateTime.UtcNow,
                request.DemandId,
                JsonSerializer.Serialize(new { reason = "CompileInvalideRefuse", request.FinishedArticleCode, request.Segment, hash }),
                "aps-segment-cbn"), cancellationToken);

            var failed = SegmentCbnEngine.Run(
                ToDemand(request),
                request.Segment,
                artifactValid: false,
                hash,
                needs,
                []);
            return new ApsSegmentCbnRunDto(null, failed, hash, ApsCompilerStatuses.Invalid);
        }

        var codes = needs.Select(n => n.ComponentCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var stocks = await cbnRepository.LoadStockAsync(codes, cancellationToken);
        var result = SegmentCbnEngine.Run(
            ToDemand(request),
            request.Segment,
            true,
            hash,
            needs,
            stocks,
            request.LotMultiple,
            request.SafetyStock);

        var runId = await cbnRepository.PersistRunAsync(result, hash, cancellationToken);
        await journalRepository.EnsureSchemaAsync(cancellationToken);

        foreach (var line in result.Lines.Where(l => l.Available > 0 || l.QtyToLaunch > 0))
        {
            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.StockMove,
                DateTime.UtcNow,
                request.DemandId,
                JsonSerializer.Serialize(new
                {
                    line.ArticleCode,
                    line.Available,
                    line.GrossNeed,
                    line.NetNeed,
                    line.QtyToLaunch,
                    line.SelectedBath,
                    note = "MouvementStock trace CBN APS (demo — pas encore apply stock)"
                }),
                "aps-segment-cbn"), cancellationToken);
        }

        // ArbitrageRegime si MTO voit du RESERVE_MTS
        if (!request.IsMtsReplenishment
            && stocks.Any(s => string.Equals(s.Status, "RESERVE_MTS", StringComparison.OrdinalIgnoreCase)))
        {
            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.RegimeArbitration,
                DateTime.UtcNow,
                request.DemandId,
                """{"note":"RESERVE_MTS detecte — non consomme sans arbitrage explicite"}""",
                "aps-segment-cbn"), cancellationToken);
        }

        return new ApsSegmentCbnRunDto(runId, result, hash, ApsCompilerStatuses.Valid);
    }

    private static ApsSegmentDemand ToDemand(ApsSegmentCbnRunRequest request)
        => new(
            request.DemandId,
            request.FinishedArticleCode,
            request.Quantity,
            request.NeedDate,
            request.CustomerCompatRule,
            request.IsMtsReplenishment,
            request.OrderAggregateId);
}

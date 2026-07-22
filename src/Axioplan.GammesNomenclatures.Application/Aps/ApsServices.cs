using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.Expectations;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Aps;

public static class ApsApplicationExtensions
{
    public static IServiceCollection AddApsApplication(this IServiceCollection services)
    {
        services.AddScoped<ApsJournalService>();
        services.AddScoped<ApsExpectationService>();
        services.AddScoped<ApsReferentialService>();
        services.AddScoped<ApsCompilerService>();
        return services;
    }
}

public sealed class ApsJournalService(IApsJournalRepository repository)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => repository.EnsureSchemaAsync(cancellationToken);

    public async Task<ApsJournalEvent> AppendFactAsync(
        AppendApsJournalFactCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.EventType))
        {
            throw new InvalidOperationException("event_type requis.");
        }

        if (string.IsNullOrWhiteSpace(command.AggregateId))
        {
            throw new InvalidOperationException("aggregate_id requis.");
        }

        if (command.OccurredAtUtc == default)
        {
            throw new InvalidOperationException("occurred_at requis.");
        }

        await repository.EnsureSchemaAsync(cancellationToken);
        return await repository.AppendFactAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ApsJournalEvent>> QueryAsync(
        ApsJournalQuery query,
        CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        return await repository.QueryFactsAsync(query, cancellationToken);
    }
}

public sealed class ApsExpectationService(IApsExpectationRepository repository)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => repository.EnsureSchemaAsync(cancellationToken);

    public async Task<ApsExpectation> EmitAsync(
        EmitApsExpectationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.ExpectedId))
        {
            throw new InvalidOperationException("expected_id requis.");
        }

        if (!ApsExpectationGrainRules.IsAllowed(command.EmittedBy, command.Grain))
        {
            throw new InvalidOperationException(
                $"Grain '{command.Grain}' non autorise pour emetteur '{command.EmittedBy}' (I-grain).");
        }

        await repository.EnsureSchemaAsync(cancellationToken);

        if (await repository.ExistsAsync(command.ExpectedId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Attendu '{command.ExpectedId}' deja emis — immuable (I-attendu). Emmettre un nouvel expected_id pour replanifier.");
        }

        return await repository.EmitAsync(command, cancellationToken);
    }

    /// <summary>
    /// Replanification = nouvel attendu (nouvel expected_id), jamais de reecriture.
    /// </summary>
    public Task<ApsExpectation> EmitReplanAsync(
        EmitApsExpectationCommand previousLogical,
        string newExpectedId,
        DateTime earliestAtUtc,
        DateTime latestAtUtc,
        string? causationId = null,
        CancellationToken cancellationToken = default)
    {
        var cmd = previousLogical with
        {
            ExpectedId = newExpectedId,
            EarliestAtUtc = earliestAtUtc,
            LatestAtUtc = latestAtUtc,
            CausationId = causationId ?? previousLogical.ExpectedId
        };
        return EmitAsync(cmd, cancellationToken);
    }

    public async Task<IReadOnlyList<ApsExpectation>> QueryAsync(
        ApsExpectationQuery query,
        CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        return await repository.QueryAsync(query, cancellationToken);
    }
}

public sealed class ApsReferentialService(IApsReferentialRepository repository)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        await repository.SeedDemoMinimalAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ApsPlaceDto>> GetPlacesAsync(CancellationToken cancellationToken = default)
        => repository.GetPlacesAsync(cancellationToken);

    public Task<IReadOnlyList<ApsStockZoneDto>> GetStockZonesAsync(CancellationToken cancellationToken = default)
        => repository.GetStockZonesAsync(cancellationToken);

    public Task<IReadOnlyList<ApsChargeCenterDto>> GetChargeCentersAsync(CancellationToken cancellationToken = default)
        => repository.GetChargeCentersAsync(cancellationToken);

    public Task<IReadOnlyList<ApsChargePostDto>> GetChargePostsAsync(CancellationToken cancellationToken = default)
        => repository.GetChargePostsAsync(cancellationToken);

    public Task<IReadOnlyList<ApsCalendarDto>> GetCalendarsAsync(CancellationToken cancellationToken = default)
        => repository.GetCalendarsAsync(cancellationToken);

    public Task<IReadOnlyList<ApsWorkRegimeDto>> GetWorkRegimesAsync(CancellationToken cancellationToken = default)
        => repository.GetWorkRegimesAsync(cancellationToken);

    public Task<IReadOnlyList<ApsTeamDto>> GetTeamsAsync(CancellationToken cancellationToken = default)
        => repository.GetTeamsAsync(cancellationToken);

    public Task<IReadOnlyList<ApsStockLotDto>> GetStockLotsAsync(CancellationToken cancellationToken = default)
        => repository.GetStockLotsAsync(cancellationToken);

    public Task<IReadOnlyList<ApsCircuitDto>> GetCircuitsAsync(CancellationToken cancellationToken = default)
        => repository.GetCircuitsAsync(cancellationToken);

    public Task<IReadOnlyList<ApsExternalEngagementDto>> GetExternalEngagementsAsync(CancellationToken cancellationToken = default)
        => repository.GetExternalEngagementsAsync(cancellationToken);

    public Task<IReadOnlyList<ApsSupplierLeadTimeDto>> GetSupplierLeadTimesAsync(CancellationToken cancellationToken = default)
        => repository.GetSupplierLeadTimesAsync(cancellationToken);
}

public sealed class ApsCompilerService(IApsCompilerRepository repository)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => repository.EnsureSchemaAsync(cancellationToken);

    public async Task<IReadOnlyList<ApsCompiledArtifactDto>> ListAsync(
        string? rootArticleCode = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        return await repository.ListArtifactsAsync(rootArticleCode, status, cancellationToken);
    }

    public async Task<ApsCompileBundle> CompileAsync(
        CompileApsRequest request,
        CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);

        var key = new ApsCompileKey(request.RootArticleCode.Trim().ToUpperInvariant(), request.Segment, request.CircuitChain);
        var bomLines = (request.BomLines ?? [])
            .Select(b => (b.ComponentCode, b.Qty, b.OperationCode))
            .ToList();
        var routing = (request.RoutingOps ?? [])
            .Select(r => (r.OperationCode, r.ResourceCode, r.UnitMinutes, r.IsBottleneck))
            .ToList();

        // Si aucune ligne fournie : artefacts squelettes vides marques TO_CONFIRM (MVP).
        if (bomLines.Count == 0)
        {
            bomLines.Add(("COMPOSANT_TO_CONFIRM", 0, null));
        }

        if (routing.Count == 0)
        {
            routing.Add(("OP_REMAILLAGE", "REMAILLAGE", 0, true));
        }

        var bundle = ApsCompilerEngine.CompileSkeleton(
            key,
            request.BomFingerprint,
            request.RoutingFingerprint,
            request.CircuitsFingerprint,
            request.YieldsFingerprint,
            request.StandardsFingerprint,
            request.EtaFingerprint,
            request.UnitsFingerprint,
            request.DecouplingFingerprint,
            bomLines,
            routing);

        await repository.PersistBundleAsync(bundle, cancellationToken);

        var sourceMarker = $"BOM:{request.BomFingerprint}|RTG:{request.RoutingFingerprint}";
        await repository.RegisterSourceDependencyAsync(
            sourceMarker,
            key.RootArticleCode,
            key.Segment,
            key.CircuitChain,
            cancellationToken);

        return bundle;
    }

    public async Task InvalidateAsync(string sourceMarker, CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        await repository.InvalidateBySourceMarkerAsync(sourceMarker, cancellationToken);
    }

    public async Task InvalidateArtifactAsync(long artifactId, CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        await repository.InvalidateArtifactAsync(artifactId, cancellationToken);
    }

    public async Task RebuildAsync(CompileApsRequest request, CancellationToken cancellationToken = default)
    {
        // Rebuild = recompile et ecrase/remplace artefacts VALID pour la meme cle (nouveaux ids).
        await CompileAsync(request, cancellationToken);
    }

    public static string SerializeNeeds(IReadOnlyList<ApsNeedLine> lines)
        => JsonSerializer.Serialize(lines);

    public static string SerializeLoads(IReadOnlyList<ApsLoadLine> lines)
        => JsonSerializer.Serialize(lines);

    public static string SerializeHre(IReadOnlyList<ApsHreLine> lines)
        => JsonSerializer.Serialize(lines);

    public static string SerializeLeadTimes(IReadOnlyList<ApsLeadTimeLine> lines)
        => JsonSerializer.Serialize(lines);
}

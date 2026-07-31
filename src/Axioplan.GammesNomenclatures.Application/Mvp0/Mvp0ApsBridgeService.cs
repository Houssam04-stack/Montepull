using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Mvp0;

public static class Mvp0ApsBridgeExtensions
{
    public static IServiceCollection AddMvp0ApsBridge(this IServiceCollection services)
    {
        services.AddScoped<Mvp0ApsBridgeService>();
        return services;
    }
}

/// <summary>
/// Pont Gate 0→1 : promotion du working set MVP-0 validé vers le référentiel APS (dossier §7.2, cahier §13.1 #1).
/// </summary>
public sealed class Mvp0ApsBridgeService(
    IMvp0Repository mvp0Repository,
    IMvp0ApsBridgeRepository bridgeRepository,
    IApsJournalRepository journalRepository,
    IApsCompilerRepository compilerRepository)
{
    public async Task<Mvp0ApsBridgeResult?> PromoteIfApprovedAsync(
        Guid campaignId,
        Mvp0GateDecision gate,
        CancellationToken cancellationToken = default)
    {
        if (gate.Outcome is not (Mvp0GateOutcomes.Go or Mvp0GateOutcomes.GoWithReservations))
        {
            return null;
        }

        var camp = await mvp0Repository.GetCampaignAsync(campaignId, cancellationToken)
                   ?? throw new InvalidOperationException("Campagne introuvable.");
        var (articles, boms, ops, _, _, centers, links) =
            await mvp0Repository.LoadWorkingSetAsync(campaignId, cancellationToken);

        if (articles.Count == 0)
        {
            return null;
        }

        var dto = ToDto(camp);
        var result = await bridgeRepository.PromoteWorkingSetAsync(
            dto, articles, boms, ops, centers, links, cancellationToken);

        await journalRepository.EnsureSchemaAsync(cancellationToken);
        await journalRepository.AppendFactAsync(
            new AppendApsJournalFactCommand(
                ApsFactEventTypes.PlanPublished,
                DateTime.UtcNow,
                camp.Code,
                JsonSerializer.Serialize(new
                {
                    kind = "Mvp0ApsBridge",
                    campaignId,
                    familyCode = camp.FamilyCode,
                    result.ArticlesUpserted,
                    result.BomLinesCreated,
                    result.RoutingOperationsCreated,
                    result.BomBaseVersion,
                    result.RoutingBaseVersion
                }),
                "Mvp0ApsBridge"),
            cancellationToken);

        await compilerRepository.EnsureSchemaAsync(cancellationToken);
        await compilerRepository.InvalidateBySourceMarkerAsync($"mvp0:{campaignId}", cancellationToken);

        return result;
    }

    private static Mvp0CampaignDto ToDto(Mvp0Campaign c) => new(
        c.Id, c.Code, c.FamilyCode, c.SiteCode, c.PeriodFrom, c.PeriodTo, c.Owner,
        c.Status, c.Provenance, c.ImportVersion, c.GateOutcome, c.CreatedAtUtc);
}

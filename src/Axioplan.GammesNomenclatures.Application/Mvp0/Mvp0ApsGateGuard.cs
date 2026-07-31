using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Mvp0;

public static class Mvp0ApsGateGuardExtensions
{
    public static IServiceCollection AddMvp0ApsGateGuard(this IServiceCollection services)
    {
        services.AddScoped<Mvp0ApsGateGuard>();
        return services;
    }
}

/// <summary>
/// Porte MVP-0 → APS (dossier §6 Gate 0→1, cahier §13.1) :
/// les calculs REAL exigent une campagne GO ou GO_WITH_RESERVATIONS.
/// </summary>
public sealed class Mvp0ApsGateGuard(IMvp0Repository repository)
{
    private static readonly HashSet<string> AllowedOutcomes = new(StringComparer.OrdinalIgnoreCase)
    {
        Mvp0GateOutcomes.Go,
        Mvp0GateOutcomes.GoWithReservations
    };

    public async Task<Mvp0CampaignDto> RequireApprovedCampaignAsync(
        CalculationSourceType sourceType,
        Guid? campaignId = null,
        CancellationToken cancellationToken = default)
    {
        if (sourceType == CalculationSourceType.Demo)
        {
            return DemoBypassCampaign();
        }

        await repository.EnsureSchemaAsync(cancellationToken);
        var campaigns = await repository.ListCampaignsAsync(cancellationToken);

        if (campaignId is Guid id)
        {
            var specific = campaigns.FirstOrDefault(c => c.Id == id);
            if (specific is null)
            {
                throw new InvalidOperationException($"Campagne MVP-0 {id} introuvable.");
            }

            if (!IsApproved(specific))
            {
                throw new InvalidOperationException(
                    $"Gate 0→1 : campagne {specific.Code} — outcome {specific.GateOutcome ?? "—"}. " +
                    "Seules GO et GO_WITH_RESERVATIONS autorisent la planification REAL.");
            }

            return specific;
        }

        var approved = campaigns
            .Where(IsApproved)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefault();

        if (approved is null)
        {
            throw new InvalidOperationException(
                "Gate 0→1 : aucune campagne MVP-0 avec GO ou GO_WITH_RESERVATIONS. " +
                "Terminez le parcours MVP-0 (validation, backtest, avis planificateur, gate) avant la planification APS REAL.");
        }

        return approved;
    }

    public static bool IsApproved(Mvp0CampaignDto campaign)
        => campaign.GateOutcome is not null && AllowedOutcomes.Contains(campaign.GateOutcome);

    private static Mvp0CampaignDto DemoBypassCampaign()
        => new(
            Guid.Empty,
            "DEMO-BYPASS",
            "DEMO",
            "SITE-DEMO",
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            "Demo",
            Mvp0CampaignStatuses.Go,
            Mvp0Provenance.Simulated,
            0,
            Mvp0GateOutcomes.Go,
            DateTime.UtcNow);
}

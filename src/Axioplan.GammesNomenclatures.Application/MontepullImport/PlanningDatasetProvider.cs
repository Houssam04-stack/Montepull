using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.MontepullImport;

public static class PlanningDatasetCodes
{
    public const string Demo = "DEMO";
    public const string MontepullReal = "MONTEPULL_REAL";
}

/// <summary>
/// Point d'entrée unique dataset actif pour CBN / Pegging / APS / Charges / MVP-0.
/// DEMO conserve seeds & sim_* ; MONTEPULL_REAL force les tables métier filtrées.
/// </summary>
public sealed class PlanningDatasetProvider(
    MontepullDatasetService datasetService,
    IMontepullImportRepository montepullRepository)
{
    public Task<string> GetActiveAsync(CancellationToken cancellationToken = default)
        => datasetService.GetActiveDatasetAsync(cancellationToken);

    public async Task<bool> IsMontepullRealAsync(CancellationToken cancellationToken = default)
    {
        var active = await GetActiveAsync(cancellationToken);
        return string.Equals(active, PlanningDatasetCodes.MontepullReal, StringComparison.OrdinalIgnoreCase);
    }

    public async Task EnsureNoDemoFallbackAsync(string caller, CancellationToken cancellationToken = default)
    {
        if (await IsMontepullRealAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Calcul impossible avec les données réelles disponibles — {caller} a tenté un fallback DEMO alors que MONTEPULL_REAL est actif.");
        }
    }

    public Task<IReadOnlyList<ApsLaunchQuantity>> ResolveMontepullLaunchesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
        => montepullRepository.ResolveMontepullLaunchesAsync(from, to, cancellationToken);

    public Task EnsureMontepullCapacityResourcesAsync(CancellationToken cancellationToken = default)
        => montepullRepository.EnsureMontepullCapacityResourcesAsync(cancellationToken);
}

public static class PlanningDatasetProviderExtensions
{
    public static IServiceCollection AddPlanningDatasetProvider(this IServiceCollection services)
    {
        services.AddScoped<PlanningDatasetProvider>();
        return services;
    }
}

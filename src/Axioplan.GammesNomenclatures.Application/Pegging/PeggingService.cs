using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Pegging;

public static class PeggingDependencyInjection
{
    public static IServiceCollection AddPeggingApplication(this IServiceCollection services)
    {
        services.AddScoped<PeggingService>();
        services.AddScoped<ConsultationService>();
        return services;
    }
}

public sealed class PeggingService(IPeggingRepository repository)
{
    public Task<PeggingRunResult> RunPeggingAsync(PeggingRunRequest request, CancellationToken cancellationToken = default)
        => repository.RunPeggingAsync(request, cancellationToken);

    public Task<IReadOnlyList<PeggingLinkRow>> GetLinksForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default)
        => repository.GetLinksForCbnRunAsync(cbnRunId, cancellationToken);

    public Task<IReadOnlyList<PeggingCoverageRow>> GetCoverageForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default)
        => repository.GetCoverageForCbnRunAsync(cbnRunId, cancellationToken);

    public Task<IReadOnlyList<PeggingSupplyUsageRow>> GetSupplyUsageForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default)
        => repository.GetSupplyUsageForCbnRunAsync(cbnRunId, cancellationToken);

    public Task<IReadOnlyList<PeggingLinkHistoryRow>> GetHistoryForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default)
        => repository.GetHistoryForCbnRunAsync(cbnRunId, cancellationToken);
}

public sealed class ConsultationService(IConsultationRepository repository)
{
    public Task<IReadOnlyList<CbnRunListItem>> GetCbnRunsAsync(CancellationToken cancellationToken = default)
        => repository.GetCbnRunsAsync(cancellationToken);

    public Task<IReadOnlyList<RoutingConsultationRow>> GetRoutingBaseAsync(string familyCode, CancellationToken cancellationToken = default)
        => repository.GetRoutingBaseAsync(familyCode, cancellationToken);

    public Task<IReadOnlyList<BomConsultationRow>> GetBomBaseAsync(string familyCode, CancellationToken cancellationToken = default)
        => repository.GetBomBaseAsync(familyCode, cancellationToken);

    public Task<IReadOnlyList<FlattenedBomConsultationRow>> GetFlattenedBomForRunAsync(int cbnRunId, CancellationToken cancellationToken = default)
        => repository.GetFlattenedBomForRunAsync(cbnRunId, cancellationToken);
}

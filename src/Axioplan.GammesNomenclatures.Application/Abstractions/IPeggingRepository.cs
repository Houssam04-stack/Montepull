using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IPeggingRepository
{
    Task<PeggingRunResult> RunPeggingAsync(PeggingRunRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeggingLinkRow>> GetLinksForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeggingCoverageRow>> GetCoverageForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeggingSupplyUsageRow>> GetSupplyUsageForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeggingLinkHistoryRow>> GetHistoryForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default);
}

public interface IConsultationRepository
{
    Task<IReadOnlyList<CbnRunListItem>> GetCbnRunsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoutingConsultationRow>> GetRoutingBaseAsync(
        string familyCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BomConsultationRow>> GetBomBaseAsync(
        string familyCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FlattenedBomConsultationRow>> GetFlattenedBomForRunAsync(
        int cbnRunId,
        CancellationToken cancellationToken = default);
}

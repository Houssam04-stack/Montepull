using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Capacity;
using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IApsFluxRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
    Task SeedDemoAsync(CancellationToken cancellationToken = default);

    Task<(bool Valid, string Hash, IReadOnlyList<ApsCompiledLoadLine> Loads)> LoadValidChargesAsync(
        string rootArticleCode,
        string segment,
        string circuitChain,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsLaunchQuantity>> ResolveLaunchesFromCbnRunAsync(
        long? segmentCbnRunId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsBufferStockLine>> LoadUpstreamBufferStockAsync(
        string chargeCenterCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsExpectedMixLine>> LoadExpectedMixAsync(CancellationToken cancellationToken = default);

    Task<(double? K, IReadOnlyList<double>? Variances, double? ReactionDays)> LoadBufferTargetParamsAsync(
        string chargeCenterCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string PlaceCode, double Occupied, double Max)>> LoadSpaceCapacitiesAsync(
        CancellationToken cancellationToken = default);

    Task<ApsBottleneckResult?> GetLastBottleneckAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task SavePublishedRunAsync(
        ApsFluxComputeResultDto result,
        CancellationToken cancellationToken = default);

    Task PersistBottleneckHistoryAsync(
        ApsBottleneckResult bn,
        bool moved,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(DateTime At, string Code, string Kind, string Explanation)>> ListBottleneckHistoryAsync(
        int take = 20,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsLoadRunSummaryDto>> ListLoadRunsAsync(int take = 20, CancellationToken cancellationToken = default);

    Task<ApsFluxComputeResultDto?> GetLoadRunAsync(long runId, CancellationToken cancellationToken = default);
}

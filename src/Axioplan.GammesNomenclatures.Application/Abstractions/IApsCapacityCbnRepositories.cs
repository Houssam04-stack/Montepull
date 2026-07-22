using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Capacity;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IApsCapacityRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
    Task SeedDemoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsCapacityResourceDto>> ListResourcesAsync(CancellationToken cancellationToken = default);
    Task<ApsCapacityResourceDto?> GetResourceAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsUnavailability>> GetUnavailabilitiesAsync(string resourceCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<double> GetEtaAsync(string resourceCode, string? familyCode, string? teamCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsCapacityReservation>> GetReservationsAsync(string resourceCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsElasticityTier>> GetTiersAsync(string resourceCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsExternalEngagementCapacity>> GetExternalEngagementsAsync(string segmentId, CancellationToken cancellationToken = default);
    Task ReserveAsync(ApsCapacityReservation reservation, CancellationToken cancellationToken = default);
    Task ReleaseAsync(string resourceCode, DateOnly bucketDate, string aggregateId, double quantity, CancellationToken cancellationToken = default);

    Task<double?> GetBathMaxFillAsync(string resourceCode, CancellationToken cancellationToken = default);
}

public interface IApsSegmentCbnRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
    Task SeedDemoAsync(CancellationToken cancellationToken = default);
    Task<(bool Valid, string Hash, IReadOnlyList<ApsCompiledNeed> Needs)> LoadValidNeedsAsync(
        string rootArticleCode,
        string segment,
        string circuitChain,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsStockPosition>> LoadStockAsync(
        IReadOnlyList<string> articleCodes,
        CancellationToken cancellationToken = default);
    Task<long> PersistRunAsync(ApsSegmentCbnResult result, string artifactHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsSegmentCbnRunHistoryDto>> ListRunHistoryAsync(int take = 20, CancellationToken cancellationToken = default);
}

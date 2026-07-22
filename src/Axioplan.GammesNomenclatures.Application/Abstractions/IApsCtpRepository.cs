using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Ctp;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IApsCtpRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
    Task SeedDemoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsRegimeDefaultRule>> LoadRegimeDefaultsAsync(CancellationToken cancellationToken = default);

    Task<ApsCtpEvaluationContext> BuildContextAsync(
        ApsCtpDemand demand,
        string effectiveRegime,
        string route,
        string? scenario,
        CancellationToken cancellationToken = default);

    /// <summary>Réservation atomique. Retourne false si rollback (aucune réservation partielle).</summary>
    Task<(bool Ok, string? Error, long? PromiseDbId)> CommitPromiseAtomicAsync(
        ApsCtpAnswer answer,
        ApsCtpDemand demand,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsCtpPromiseDto>> ListPromisesAsync(CancellationToken cancellationToken = default);

    Task<(bool Ok, string? Error)> ReleasePromiseDemoAsync(
        long promiseDbId,
        CancellationToken cancellationToken = default);

    Task<(bool Valid, double? HrePerUnit, string Status)> LoadHreAsync(
        string articleCode,
        CancellationToken cancellationToken = default);
}

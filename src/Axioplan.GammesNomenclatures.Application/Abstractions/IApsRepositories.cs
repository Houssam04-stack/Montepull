using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.Expectations;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IApsJournalRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<ApsJournalEvent> AppendFactAsync(
        AppendApsJournalFactCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsJournalEvent>> QueryFactsAsync(
        ApsJournalQuery query,
        CancellationToken cancellationToken = default);
}

public interface IApsExpectationRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<ApsExpectation> EmitAsync(
        EmitApsExpectationCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsExpectation>> QueryAsync(
        ApsExpectationQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Returns true if expected_id already exists (immutability check).</summary>
    Task<bool> ExistsAsync(string expectedId, CancellationToken cancellationToken = default);
}

public interface IApsReferentialRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsPlaceDto>> GetPlacesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsStockZoneDto>> GetStockZonesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsChargeCenterDto>> GetChargeCentersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsChargePostDto>> GetChargePostsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsCalendarDto>> GetCalendarsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsWorkRegimeDto>> GetWorkRegimesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsTeamDto>> GetTeamsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsStockLotDto>> GetStockLotsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsCircuitDto>> GetCircuitsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsExternalEngagementDto>> GetExternalEngagementsAsync(CancellationToken cancellationToken = default);

    Task<ApsResourceScheduleDto> GetResourceScheduleAsync(string resourceCode, CancellationToken cancellationToken = default);

    Task SeedDemoMinimalAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsSupplierLeadTimeDto>> GetSupplierLeadTimesAsync(CancellationToken cancellationToken = default);

    Task<int> UpsertPlaceAsync(string code, string label, string level, int? parentId, CancellationToken cancellationToken = default);
}

public interface IApsCompilerRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApsCompiledArtifactDto>> ListArtifactsAsync(
        string? rootArticleCode = null,
        string? status = null,
        CancellationToken cancellationToken = default);

    Task PersistBundleAsync(ApsCompileBundle bundle, CancellationToken cancellationToken = default);

    Task InvalidateBySourceMarkerAsync(string sourceMarker, CancellationToken cancellationToken = default);

    Task InvalidateArtifactAsync(long artifactId, CancellationToken cancellationToken = default);

    Task RegisterSourceDependencyAsync(
        string sourceMarker,
        string rootArticleCode,
        string segment,
        string circuitChain,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDependentCompileKeysAsync(
        string sourceMarker,
        CancellationToken cancellationToken = default);
}

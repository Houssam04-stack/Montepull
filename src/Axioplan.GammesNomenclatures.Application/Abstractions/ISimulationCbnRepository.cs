using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface ISimulationCbnRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimulationHeaderDto>> ListSimulationsAsync(CancellationToken cancellationToken = default);

    Task<SimulationHeaderDto> CreateSimulationAsync(CreateSimulationRequest request, CancellationToken cancellationToken = default);

    Task DeleteSimulationAsync(int simulationId, CancellationToken cancellationToken = default);

    Task<SimulationHeaderDto?> GetSimulationAsync(int simulationId, CancellationToken cancellationToken = default);

    Task ClearCbnResultsAsync(int simulationId, CancellationToken cancellationToken = default);

    Task SeedPantalonDemoAsync(int simulationId, CancellationToken cancellationToken = default);

    Task<SeedTemplateArticleResult> SeedTemplateArticleAsync(SeedTemplateArticleRequest request, CancellationToken cancellationToken = default);

    Task<SimulationArticleDto> CreateSimArticleAsync(CreateSimArticleRequest request, CancellationToken cancellationToken = default);

    Task<SimulationBomLineDto> UpsertSimBomLineAsync(UpsertSimBomLineRequest request, CancellationToken cancellationToken = default);

    Task DeleteSimBomLineAsync(DeleteSimBomLineRequest request, CancellationToken cancellationToken = default);

    Task EnsureAlternativeNomenclaturesAsync(int simulationId, int parentArticleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimulationArticleDto>> GetArticlesAsync(int simulationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimulationBomLineDto>> GetBomLinesAsync(int simulationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimulationRoutingOpDto>> GetRoutingOpsAsync(int simulationId, int? articleId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimulationCbnParameterDto>> GetCbnParametersAsync(int simulationId, CancellationToken cancellationToken = default);

    Task UpdateCbnParameterAsync(UpdateSimCbnParameterRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimulationSalesOrderDto>> GetSalesOrdersAsync(int simulationId, CancellationToken cancellationToken = default);

    Task<SimulationSalesOrderDto> UpsertSalesOrderAsync(UpsertSalesOrderRequest request, CancellationToken cancellationToken = default);

    Task<GenerateVariantsResult> GenerateVariantsAsync(GenerateVariantsRequest request, CancellationToken cancellationToken = default);

    Task<CbnRunResultDto> RunCbnAsync(CbnRunRequestDto request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimCbnRunHistoryDto>> ListCbnRunsAsync(int simulationId, CancellationToken cancellationToken = default);

    Task<CbnRunResultDto?> GetCbnRunResultAsync(int runId, CancellationToken cancellationToken = default);

    Task<DuplicationOptionsDto> GetDuplicationOptionsAsync(int simulationId, CancellationToken cancellationToken = default);

    Task SaveDuplicationOptionsAsync(SaveDuplicationOptionsRequest request, CancellationToken cancellationToken = default);

    Task EnsureDefaultDuplicationOptionsAsync(int simulationId, CancellationToken cancellationToken = default);
}

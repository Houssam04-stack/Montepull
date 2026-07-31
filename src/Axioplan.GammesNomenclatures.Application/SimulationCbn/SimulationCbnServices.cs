using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.SimulationCbn;

public static class SimulationCbnDependencyInjection
{
    public static IServiceCollection AddSimulationCbnApplication(this IServiceCollection services)
    {
        services.AddScoped<SimulationOrchestrationService>();
        services.AddScoped<DuplicationService>();
        services.AddScoped<SimulationMrpCbnService>();
        return services;
    }
}

/// <summary>
/// SimulationService : creer/charger une simulation, nettoyer les resultats CBN avant relance.
/// </summary>
public sealed class SimulationOrchestrationService(ISimulationCbnRepository repository)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => repository.EnsureSchemaAsync(cancellationToken);

    public Task<IReadOnlyList<SimulationHeaderDto>> ListAsync(CancellationToken cancellationToken = default)
        => repository.ListSimulationsAsync(cancellationToken);

    public Task<SimulationHeaderDto> CreateAsync(CreateSimulationRequest request, CancellationToken cancellationToken = default)
        => repository.CreateSimulationAsync(request, cancellationToken);

    public Task DeleteAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.DeleteSimulationAsync(simulationId, cancellationToken);

    public Task<SimulationHeaderDto?> GetAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.GetSimulationAsync(simulationId, cancellationToken);

    public Task ClearCbnResultsAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.ClearCbnResultsAsync(simulationId, cancellationToken);

    public Task SeedPantalonDemoAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.SeedPantalonDemoAsync(simulationId, cancellationToken);

    public Task<SimulationHeaderDto> EnsureTunimapulfSimulationAsync(CancellationToken cancellationToken = default)
        => repository.EnsureTunimapulfSimulationAsync(cancellationToken);

    public Task SyncStocksIntoSimCbnParametersAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.SyncStocksIntoSimCbnParametersAsync(simulationId, cancellationToken);

    public Task<SeedTemplateArticleResult> SeedTemplateArticleAsync(SeedTemplateArticleRequest request, CancellationToken cancellationToken = default)
        => repository.SeedTemplateArticleAsync(request, cancellationToken);

    public Task<SimulationArticleDto> CreateSimArticleAsync(CreateSimArticleRequest request, CancellationToken cancellationToken = default)
        => repository.CreateSimArticleAsync(request, cancellationToken);

    public Task<SimulationBomLineDto> UpsertSimBomLineAsync(UpsertSimBomLineRequest request, CancellationToken cancellationToken = default)
        => repository.UpsertSimBomLineAsync(request, cancellationToken);

    public Task DeleteSimBomLineAsync(DeleteSimBomLineRequest request, CancellationToken cancellationToken = default)
        => repository.DeleteSimBomLineAsync(request, cancellationToken);

    public Task EnsureAlternativeNomenclaturesAsync(int simulationId, int parentArticleId, CancellationToken cancellationToken = default)
        => repository.EnsureAlternativeNomenclaturesAsync(simulationId, parentArticleId, cancellationToken);

    public Task<IReadOnlyList<SimulationArticleDto>> GetArticlesAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.GetArticlesAsync(simulationId, cancellationToken);

    public Task<IReadOnlyList<SimulationBomLineDto>> GetBomLinesAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.GetBomLinesAsync(simulationId, cancellationToken);

    public Task<IReadOnlyList<SimulationRoutingOpDto>> GetRoutingOpsAsync(int simulationId, int? articleId = null, CancellationToken cancellationToken = default)
        => repository.GetRoutingOpsAsync(simulationId, articleId, cancellationToken);

    public Task<IReadOnlyList<SimulationCbnParameterDto>> GetParametersAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.GetCbnParametersAsync(simulationId, cancellationToken);

    public Task UpdateParameterAsync(UpdateSimCbnParameterRequest request, CancellationToken cancellationToken = default)
        => repository.UpdateCbnParameterAsync(request, cancellationToken);

    public Task<IReadOnlyList<SimulationSalesOrderDto>> GetSalesOrdersAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.GetSalesOrdersAsync(simulationId, cancellationToken);

    public Task<SimulationSalesOrderDto> UpsertSalesOrderAsync(UpsertSalesOrderRequest request, CancellationToken cancellationToken = default)
        => repository.UpsertSalesOrderAsync(request, cancellationToken);

    public Task<DuplicationOptionsDto> GetDuplicationOptionsAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.GetDuplicationOptionsAsync(simulationId, cancellationToken);

    public Task SaveDuplicationOptionsAsync(SaveDuplicationOptionsRequest request, CancellationToken cancellationToken = default)
        => repository.SaveDuplicationOptionsAsync(request, cancellationToken);

    public Task EnsureDefaultDuplicationOptionsAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.EnsureDefaultDuplicationOptionsAsync(simulationId, cancellationToken);
}

/// <summary>
/// DuplicationService : variantes taille/couleur + BOM + gamme + params CBN.
/// </summary>
public sealed class DuplicationService(ISimulationCbnRepository repository)
{
    public Task<GenerateVariantsResult> GenerateVariantsAsync(
        GenerateVariantsRequest request,
        CancellationToken cancellationToken = default)
        => repository.GenerateVariantsAsync(request, cancellationToken);
}

/// <summary>
/// CbnService simulation : LLC, explosion controlee, brut, net, OF/OA, pegging.
/// </summary>
public sealed class SimulationMrpCbnService(ISimulationCbnRepository repository)
{
    public Task ClearAndRunAsync(CbnRunRequestDto request, CancellationToken cancellationToken = default)
        => RunAsync(request, cancellationToken);

    public Task<CbnRunResultDto> RunAsync(CbnRunRequestDto request, CancellationToken cancellationToken = default)
        => repository.RunCbnAsync(request, cancellationToken);

    public Task<IReadOnlyList<SimCbnRunHistoryDto>> ListRunsAsync(int simulationId, CancellationToken cancellationToken = default)
        => repository.ListCbnRunsAsync(simulationId, cancellationToken);

    public Task<CbnRunResultDto?> GetRunAsync(int runId, CancellationToken cancellationToken = default)
        => repository.GetCbnRunResultAsync(runId, cancellationToken);
}

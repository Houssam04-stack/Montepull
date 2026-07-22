using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Cbn;

/// <summary>
/// Point d'entrée unique « CBN — Calcul des besoins nets ».
/// Délègue aux moteurs existants selon le contexte — aucune logique CBN nouvelle.
/// </summary>
public interface ICbnApplicationService
{
    // --- REAL (CbnEngine / cbn_*) — compatible pegging ---

    Task<IReadOnlyList<SalesOrderListItem>> GetRealSalesOrdersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesOrderLineItem>> GetRealSalesOrderLinesAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default);

    Task<CbnApplicationRunResult<CbnRunResult>> RunRealAsync(
        CbnRunRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CbnRunListItem>> ListRealRunsAsync(CancellationToken cancellationToken = default);

    // --- SIMULATION (SimulationCbnEngine / sim_cbn_*) ---

    Task<CbnApplicationRunResult<CbnRunResultDto>> RunSimulationAsync(
        CbnRunRequestDto request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimCbnRunHistoryDto>> ListSimulationRunsAsync(
        int simulationId,
        CancellationToken cancellationToken = default);

    Task<CbnRunResultDto?> GetSimulationRunAsync(int runId, CancellationToken cancellationToken = default);
}

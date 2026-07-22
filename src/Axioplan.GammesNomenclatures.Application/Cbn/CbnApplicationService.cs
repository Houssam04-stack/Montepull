using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.Pegging;
using Axioplan.GammesNomenclatures.Application.SimulationCbn;

namespace Axioplan.GammesNomenclatures.Application.Cbn;

public sealed class CbnApplicationService(
    CbnService realCbn,
    SimulationMrpCbnService simulationCbn,
    ConsultationService consultation) : ICbnApplicationService
{
    public Task<IReadOnlyList<SalesOrderListItem>> GetRealSalesOrdersAsync(CancellationToken cancellationToken = default)
        => realCbn.GetSalesOrdersAsync(cancellationToken);

    public Task<IReadOnlyList<SalesOrderLineItem>> GetRealSalesOrderLinesAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default)
        => realCbn.GetSalesOrderLinesAsync(salesOrderId, cancellationToken);

    public async Task<CbnApplicationRunResult<CbnRunResult>> RunRealAsync(
        CbnRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await realCbn.RunCbnAsync(request, cancellationToken);
        var context = new CalculationRunContext
        {
            RunId = FormatRunId(CalculationSourceType.Real, result.RunId),
            SourceType = CalculationSourceType.Real,
            ExistingCbnRunId = result.RunId,
            Status = result.Status
        };
        return new CbnApplicationRunResult<CbnRunResult>(result, context);
    }

    public Task<IReadOnlyList<CbnRunListItem>> ListRealRunsAsync(CancellationToken cancellationToken = default)
        => consultation.GetCbnRunsAsync(cancellationToken);

    public async Task<CbnApplicationRunResult<CbnRunResultDto>> RunSimulationAsync(
        CbnRunRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await simulationCbn.RunAsync(request, cancellationToken);
        var context = new CalculationRunContext
        {
            RunId = FormatRunId(CalculationSourceType.Simulation, result.RunId),
            SourceType = CalculationSourceType.Simulation,
            ExistingCbnRunId = result.RunId,
            Status = "COMPLETED"
        };
        return new CbnApplicationRunResult<CbnRunResultDto>(result, context);
    }

    public Task<IReadOnlyList<SimCbnRunHistoryDto>> ListSimulationRunsAsync(
        int simulationId,
        CancellationToken cancellationToken = default)
        => simulationCbn.ListRunsAsync(simulationId, cancellationToken);

    public Task<CbnRunResultDto?> GetSimulationRunAsync(int runId, CancellationToken cancellationToken = default)
        => simulationCbn.GetRunAsync(runId, cancellationToken);

    internal static string FormatRunId(CalculationSourceType sourceType, int runId) =>
        $"{sourceType.ToString().ToUpperInvariant()}-{runId}";
}

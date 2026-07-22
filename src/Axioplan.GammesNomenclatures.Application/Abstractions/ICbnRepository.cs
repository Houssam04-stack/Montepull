using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface ICbnRepository
{
    Task<IReadOnlyList<SalesOrderListItem>> GetSalesOrdersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesOrderLineItem>> GetSalesOrderLinesAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default);

    Task<CbnRunResult> RunCbnAsync(CbnRunRequest request, CancellationToken cancellationToken = default);
}

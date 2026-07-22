using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Cbn;

public static class CbnDependencyInjection
{
    public static IServiceCollection AddCbnApplication(this IServiceCollection services)
    {
        services.AddScoped<CbnService>();
        services.AddScoped<ICbnApplicationService, CbnApplicationService>();
        return services;
    }
}

public sealed class CbnService(ICbnRepository repository)
{
    public Task<IReadOnlyList<SalesOrderListItem>> GetSalesOrdersAsync(CancellationToken cancellationToken = default)
        => repository.GetSalesOrdersAsync(cancellationToken);

    public Task<IReadOnlyList<SalesOrderLineItem>> GetSalesOrderLinesAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default)
        => repository.GetSalesOrderLinesAsync(salesOrderId, cancellationToken);

    public Task<CbnRunResult> RunCbnAsync(CbnRunRequest request, CancellationToken cancellationToken = default)
        => repository.RunCbnAsync(request, cancellationToken);
}

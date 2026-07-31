using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.MontepullImport;
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

public sealed class CbnService(ICbnRepository repository, PlanningDatasetProvider dataset)
{
    public async Task<IReadOnlyList<SalesOrderListItem>> GetSalesOrdersAsync(CancellationToken cancellationToken = default)
    {
        var filter = await dataset.IsMontepullRealAsync(cancellationToken)
            ? PlanningDatasetCodes.MontepullReal
            : null;
        return await repository.GetSalesOrdersAsync(filter, cancellationToken);
    }

    public Task<IReadOnlyList<SalesOrderLineItem>> GetSalesOrderLinesAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default)
        => repository.GetSalesOrderLinesAsync(salesOrderId, cancellationToken);

    public async Task<IReadOnlyList<ImportedSalesOrderDetail>> GetImportedSalesOrdersDetailAsync(
        CancellationToken cancellationToken = default)
    {
        var filter = await dataset.IsMontepullRealAsync(cancellationToken)
            ? PlanningDatasetCodes.MontepullReal
            : null;
        return await repository.GetImportedSalesOrdersDetailAsync(filter, cancellationToken);
    }

    public Task EnsureTunimapulfSalesOrderAsync(CancellationToken cancellationToken = default)
        => repository.EnsureTunimapulfSalesOrderAsync(cancellationToken);

    public async Task<ImportedSalesOrderDetail> UpsertImportedSalesOrderLineAsync(
        UpsertImportedSalesOrderLineRequest request,
        CancellationToken cancellationToken = default)
    {
        var dataSource = request.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            dataSource = await dataset.IsMontepullRealAsync(cancellationToken)
                ? PlanningDatasetCodes.MontepullReal
                : "DEMO";
        }

        return await repository.UpsertImportedSalesOrderLineAsync(
            request with { DataSource = dataSource },
            cancellationToken);
    }

    public Task<CbnRunResult> RunCbnAsync(CbnRunRequest request, CancellationToken cancellationToken = default)
        => repository.RunCbnAsync(request, cancellationToken);
}

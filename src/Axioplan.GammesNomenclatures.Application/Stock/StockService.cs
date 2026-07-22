using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Stock;

public static class StockDependencyInjection
{
    public static IServiceCollection AddStockApplication(this IServiceCollection services)
    {
        services.AddScoped<StockService>();
        return services;
    }
}

public sealed class StockService(IStockRepository repository)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => repository.EnsureSchemaAsync(cancellationToken);

    public Task<IReadOnlyList<StockBalanceRow>> GetStockBalancesAsync(CancellationToken cancellationToken = default)
        => repository.GetStockBalancesAsync(cancellationToken);

    public Task<IReadOnlyList<StockLotLineRow>> GetStockLotLinesAsync(CancellationToken cancellationToken = default)
        => repository.GetStockLotLinesAsync(cancellationToken);

    public Task<IReadOnlyList<StockNomenclatureLineRow>> GetNomenclatureLinesAsync(CancellationToken cancellationToken = default)
        => repository.GetNomenclatureLinesAsync(cancellationToken);

    public Task<StockFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
        => repository.GetFilterOptionsAsync(cancellationToken);
}

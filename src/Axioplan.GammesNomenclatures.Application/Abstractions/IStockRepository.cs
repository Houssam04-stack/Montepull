using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IStockRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockBalanceRow>> GetStockBalancesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockLotLineRow>> GetStockLotLinesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockNomenclatureLineRow>> GetNomenclatureLinesAsync(CancellationToken cancellationToken = default);

    Task<StockFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
}

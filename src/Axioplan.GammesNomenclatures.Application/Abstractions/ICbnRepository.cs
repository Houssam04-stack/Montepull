using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface ICbnRepository
{
    Task<IReadOnlyList<SalesOrderListItem>> GetSalesOrdersAsync(
        string? dataSourceFilter = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesOrderLineItem>> GetSalesOrderLinesAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Liste détaillée des commandes importées (en-têtes + lignes), filtrée par data_source.
    /// </summary>
    Task<IReadOnlyList<ImportedSalesOrderDetail>> GetImportedSalesOrdersDetailAsync(
        string? dataSourceFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Garantit la commande métier DEMO CMD-TUNIMAPULF / article TUNIMAPULF.</summary>
    Task EnsureTunimapulfSalesOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>Crée ou met à jour une ligne de commande (champs type commandes.xlsx).</summary>
    Task<ImportedSalesOrderDetail> UpsertImportedSalesOrderLineAsync(
        UpsertImportedSalesOrderLineRequest request,
        CancellationToken cancellationToken = default);

    Task<CbnRunResult> RunCbnAsync(CbnRunRequest request, CancellationToken cancellationToken = default);
}

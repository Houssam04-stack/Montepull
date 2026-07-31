using Axioplan.GammesNomenclatures.Application.Models.Commandes;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface ICommandeRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandeListItem>> GetCommandesAsync(CancellationToken cancellationToken = default);

    Task<CommandeDetailDto?> GetCommandeAsync(int id, CancellationToken cancellationToken = default);

    Task<CommandeDetailDto?> GetCommandeBySoAsync(string soCode, CancellationToken cancellationToken = default);

    Task<string> PeekNextSoCodeAsync(CancellationToken cancellationToken = default);

    Task<CommandeDetailDto> CreateCommandeAsync(
        CreateCommandeRequest request,
        CancellationToken cancellationToken = default);

    Task<CommandeDetailDto> UpdateCommandeAsync(
        UpdateCommandeRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandeHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default);

    Task<int> CountUnreadHistoryAsync(CancellationToken cancellationToken = default);

    Task MarkHistoryVisitedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArticlePickItem>> SearchArticlesAsync(
        string? search,
        int take = 40,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string Code, string Label)>> GetCustomersAsync(CancellationToken cancellationToken = default);
}

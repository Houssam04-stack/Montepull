using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models.Commandes;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Commandes;

public static class CommandeApplicationExtensions
{
    public static IServiceCollection AddCommandeApplication(this IServiceCollection services)
    {
        services.AddScoped<CommandeService>();
        return services;
    }
}

public sealed class CommandeService(ICommandeRepository repository)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => repository.EnsureSchemaAsync(cancellationToken);

    public Task<IReadOnlyList<CommandeListItem>> GetCommandesAsync(CancellationToken cancellationToken = default)
        => repository.GetCommandesAsync(cancellationToken);

    public Task<CommandeDetailDto?> GetCommandeAsync(int id, CancellationToken cancellationToken = default)
        => repository.GetCommandeAsync(id, cancellationToken);

    public Task<CommandeDetailDto?> GetCommandeBySoAsync(string soCode, CancellationToken cancellationToken = default)
        => repository.GetCommandeBySoAsync(soCode, cancellationToken);

    public Task<string> PeekNextSoCodeAsync(CancellationToken cancellationToken = default)
        => repository.PeekNextSoCodeAsync(cancellationToken);

    public Task<CommandeDetailDto> CreateCommandeAsync(
        CreateCommandeRequest request,
        CancellationToken cancellationToken = default)
        => repository.CreateCommandeAsync(request, cancellationToken);

    public Task<CommandeDetailDto> UpdateCommandeAsync(
        UpdateCommandeRequest request,
        CancellationToken cancellationToken = default)
        => repository.UpdateCommandeAsync(request, cancellationToken);

    public Task<IReadOnlyList<CommandeHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default)
        => repository.GetHistoryAsync(cancellationToken);

    public Task<int> CountUnreadHistoryAsync(CancellationToken cancellationToken = default)
        => repository.CountUnreadHistoryAsync(cancellationToken);

    public Task MarkHistoryVisitedAsync(CancellationToken cancellationToken = default)
        => repository.MarkHistoryVisitedAsync(cancellationToken);

    public Task<IReadOnlyList<ArticlePickItem>> SearchArticlesAsync(
        string? search,
        int take = 40,
        CancellationToken cancellationToken = default)
        => repository.SearchArticlesAsync(search, take, cancellationToken);

    public Task<IReadOnlyList<(string Code, string Label)>> GetCustomersAsync(
        CancellationToken cancellationToken = default)
        => repository.GetCustomersAsync(cancellationToken);
}

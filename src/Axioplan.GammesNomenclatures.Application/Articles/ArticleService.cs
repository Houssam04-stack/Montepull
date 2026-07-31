using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models.Articles;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application;

public static class ArticleDependencyInjection
{
    public static IServiceCollection AddArticleApplication(this IServiceCollection services)
    {
        services.AddScoped<ArticleService>();
        return services;
    }
}

public sealed class ArticleService(IArticleRepository repository)
{
    public Task<IReadOnlyList<ArticleListItem>> GetArticlesAsync(CancellationToken cancellationToken = default)
        => repository.GetArticlesAsync(cancellationToken);

    public Task<IReadOnlyList<CategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        => repository.GetCategoriesAsync(cancellationToken);

    public Task<IReadOnlyList<FamilyItem>> GetFamiliesAsync(CancellationToken cancellationToken = default)
        => repository.GetFamiliesAsync(cancellationToken);

    public Task<IReadOnlyList<CustomerItem>> GetCustomersAsync(CancellationToken cancellationToken = default)
        => repository.GetCustomersAsync(cancellationToken);

    public Task<IReadOnlyList<AttributeDefinitionItem>> GetAttributeDefinitionsAsync(CancellationToken cancellationToken = default)
        => repository.GetAttributeDefinitionsAsync(cancellationToken);

    public Task<IReadOnlyList<AttributeOptionItem>> GetAttributeOptionsAsync(
        string? attributeCode = null,
        CancellationToken cancellationToken = default)
        => repository.GetAttributeOptionsAsync(attributeCode, cancellationToken);

    public Task<IReadOnlyList<AttributeFormattingRuleItem>> GetFormattingRulesAsync(CancellationToken cancellationToken = default)
        => repository.GetFormattingRulesAsync(cancellationToken);

    public Task<IReadOnlyList<FamilyAttributeItem>> GetFamilyAttributesAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
        => repository.GetFamilyAttributesAsync(familyCode, cancellationToken);

    public Task<IReadOnlyList<CustomerMatrixAttributeItem>> GetCustomerMatrixAsync(
        string customerCode,
        string familyCode,
        string? seasonCode = null,
        CancellationToken cancellationToken = default)
        => repository.GetCustomerMatrixAsync(customerCode, familyCode, seasonCode, cancellationToken);

    public Task<FormatValueResult> FormatValueAsync(FormatValueRequest request, CancellationToken cancellationToken = default)
        => repository.FormatValueAsync(request, cancellationToken);

    public Task<ArticleListItem> CreateManualArticleAsync(
        CreateManualArticleRequest request,
        CancellationToken cancellationToken = default)
        => repository.CreateManualArticleAsync(request, cancellationToken);

    public Task<ConfigureArticlesResult> ConfigureArticlesAsync(
        ConfigureArticlesRequest request,
        CancellationToken cancellationToken = default)
        => repository.ConfigureArticlesAsync(request, cancellationToken);

    public Task<DuplicateArticleResult> DuplicateArticleAsync(
        DuplicateArticleRequest request,
        CancellationToken cancellationToken = default)
        => repository.DuplicateArticleAsync(request, cancellationToken);

    public Task<CategoryItem> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
        => repository.CreateCategoryAsync(request, cancellationToken);

    public Task<FamilyItem> CreateFamilyAsync(CreateFamilyRequest request, CancellationToken cancellationToken = default)
        => repository.CreateFamilyAsync(request, cancellationToken);

    public Task<AttributeDefinitionItem> CreateAttributeAsync(
        CreateAttributeRequest request,
        CancellationToken cancellationToken = default)
        => repository.CreateAttributeAsync(request, cancellationToken);

    public Task<AttributeOptionItem> CreateAttributeOptionAsync(
        CreateAttributeOptionRequest request,
        CancellationToken cancellationToken = default)
        => repository.CreateAttributeOptionAsync(request, cancellationToken);

    public Task<IReadOnlyList<TraceabilityItem>> GetTraceabilityAsync(CancellationToken cancellationToken = default)
        => repository.GetTraceabilityAsync(cancellationToken);

    public Task<IReadOnlyList<BomLinkInfo>> GetBomLinksAsync(CancellationToken cancellationToken = default)
        => repository.GetBomLinksAsync(cancellationToken);

    public Task<ArticleBomEditorDto> GetBomForArticleAsync(
        int finishedGoodArticleId,
        CancellationToken cancellationToken = default)
        => repository.GetBomForArticleAsync(finishedGoodArticleId, cancellationToken);

    public Task<ArticleBomLineDto> UpsertArticleBomLineAsync(
        UpsertArticleBomLineRequest request,
        CancellationToken cancellationToken = default)
        => repository.UpsertArticleBomLineAsync(request, cancellationToken);

    public Task DeleteArticleBomLineAsync(int bomLineId, CancellationToken cancellationToken = default)
        => repository.DeleteArticleBomLineAsync(bomLineId, cancellationToken);
}

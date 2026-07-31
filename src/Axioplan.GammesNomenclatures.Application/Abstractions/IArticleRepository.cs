using Axioplan.GammesNomenclatures.Application.Models.Articles;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IArticleRepository
{
    Task<IReadOnlyList<ArticleListItem>> GetArticlesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FamilyItem>> GetFamiliesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerItem>> GetCustomersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttributeDefinitionItem>> GetAttributeDefinitionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttributeOptionItem>> GetAttributeOptionsAsync(
        string? attributeCode = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttributeFormattingRuleItem>> GetFormattingRulesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FamilyAttributeItem>> GetFamilyAttributesAsync(
        string familyCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerMatrixAttributeItem>> GetCustomerMatrixAsync(
        string customerCode,
        string familyCode,
        string? seasonCode = null,
        CancellationToken cancellationToken = default);

    Task<FormatValueResult> FormatValueAsync(FormatValueRequest request, CancellationToken cancellationToken = default);

    Task<ArticleListItem> CreateManualArticleAsync(
        CreateManualArticleRequest request,
        CancellationToken cancellationToken = default);

    Task<ConfigureArticlesResult> ConfigureArticlesAsync(
        ConfigureArticlesRequest request,
        CancellationToken cancellationToken = default);

    Task<DuplicateArticleResult> DuplicateArticleAsync(
        DuplicateArticleRequest request,
        CancellationToken cancellationToken = default);

    Task<CategoryItem> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<FamilyItem> CreateFamilyAsync(CreateFamilyRequest request, CancellationToken cancellationToken = default);

    Task<AttributeDefinitionItem> CreateAttributeAsync(
        CreateAttributeRequest request,
        CancellationToken cancellationToken = default);

    Task<AttributeOptionItem> CreateAttributeOptionAsync(
        CreateAttributeOptionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TraceabilityItem>> GetTraceabilityAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BomLinkInfo>> GetBomLinksAsync(CancellationToken cancellationToken = default);

    Task<ArticleBomEditorDto> GetBomForArticleAsync(int finishedGoodArticleId, CancellationToken cancellationToken = default);

    Task<ArticleBomLineDto> UpsertArticleBomLineAsync(
        UpsertArticleBomLineRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteArticleBomLineAsync(int bomLineId, CancellationToken cancellationToken = default);
}

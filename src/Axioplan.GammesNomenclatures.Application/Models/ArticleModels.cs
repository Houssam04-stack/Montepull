namespace Axioplan.GammesNomenclatures.Application.Models.Articles;

public sealed record ArticleListItem(
    int Id,
    string Code,
    string Label,
    string FamilyCode,
    string FamilyLabel,
    string CategoryCode,
    string ArticleType,
    string DefaultUnit,
    string CreationMode,
    string? CustomerCode,
    string? SourceArticleCode,
    DateTime CreatedAt,
    int? ConfigurationSessionId = null,
    int? DuplicationSessionId = null);

public sealed record CategoryItem(int Id, string Code, string Label);

public sealed record FamilyItem(
    int Id,
    string Code,
    string Label,
    string CategoryCode,
    string CategoryLabel);

public sealed record CustomerItem(int Id, string Code, string Label);

public sealed record AttributeDefinitionItem(
    int Id,
    string Code,
    string Label,
    string ValueType,
    string SelectionMode,
    bool IsGenerator);

public sealed record AttributeOptionItem(
    int Id,
    int AttributeId,
    string DisplayValue,
    string NormalizedValue,
    string TechnicalCode,
    string Status);

public sealed record AttributeFormattingRuleItem(
    int AttributeId,
    string AttributeCode,
    bool TrimSpaces,
    bool CollapseSpaces,
    bool RemoveInternalSpaces,
    string CaseRule,
    bool StripAccentsForCode,
    string? ForbiddenChars);

public sealed record FamilyAttributeItem(
    int AttributeId,
    string AttributeCode,
    string AttributeLabel,
    int Position,
    bool IsVisible,
    bool IsRequired,
    bool IsGenerator,
    bool AllowsMultiSelect,
    bool AllowsCreate);

public sealed record CustomerMatrixAttributeItem(
    int AttributeId,
    string AttributeCode,
    string AttributeLabel,
    bool IsVisible,
    bool IsRequired,
    bool IsModifiable,
    bool AllowsMultiSelect,
    bool AllowsCreate,
    bool IsGenerator,
    bool RoleInCode,
    bool RoleInDescription,
    bool RoleInBom,
    bool RoleInPegging,
    string ValidationStatus,
    string SourceLevel);

public sealed record CreateManualArticleRequest(
    string Code,
    string Label,
    string FamilyCode,
    string ArticleType,
    string DefaultUnit,
    int? CustomerId = null);

public sealed record ConfigureArticleInput(
    string AttributeCode,
    string RawValue);

public sealed record ConfigureArticlesRequest(
    string CustomerCode,
    string FamilyCode,
    string FinishedGoodCode,
    IReadOnlyList<string> SizeValues,
    IReadOnlyList<string> ColorValues,
    string CompositionRaw,
    string CareCode,
    IReadOnlyList<string>? LanguageValues = null,
    string? CustomerOrderCode = null);

public sealed record ConfigureArticlesResult(
    int SessionId,
    int GeneratedCount,
    IReadOnlyList<ArticleListItem> Articles,
    string BomLinkStatus,
    string? BomLinkNote);

public sealed record DuplicateArticleRequest(
    int SourceArticleId,
    string NewCode,
    string? NewLabel = null,
    bool CopyBom = false,
    bool CopyRouting = false);

public sealed record DuplicateArticleResult(
    int SessionId,
    ArticleListItem Article,
    IReadOnlyList<DuplicationChangeItem> Changes,
    string? BomLinkNote);

public sealed record DuplicationChangeItem(
    string AttributeCode,
    string? OldDisplayValue,
    string? NewDisplayValue);

public sealed record FormatValueRequest(string AttributeCode, string RawValue);

public sealed record FormatValueResult(
    string RawValue,
    string DisplayValue,
    string NormalizedValue,
    string TechnicalCode);

public sealed record CreateCategoryRequest(string Code, string Label);

public sealed record CreateFamilyRequest(string Code, string Label, string CategoryCode);

public sealed record CreateAttributeRequest(
    string Code,
    string Label,
    string ValueType,
    string SelectionMode,
    bool IsGenerator);

public sealed record CreateAttributeOptionRequest(
    string AttributeCode,
    string RawValue,
    string Status = "TO_VALIDATE");

public sealed record TraceabilityItem(
    string TraceType,
    int SessionId,
    DateTime CreatedAt,
    string? CustomerCode,
    string? FamilyCode,
    string? SourceArticleCode,
    string? NewArticleCode,
    int ArticleCount,
    string Status,
    string? Notes);

public sealed record BomLinkInfo(
    string ArticleCode,
    string? ProductFamilyCode,
    string? BomBaseCode,
    string? RoutingBaseCode,
    string LinkStatus);

/// <summary>Nomenclature métier d'un article (produit fini).</summary>
public sealed record ArticleBomEditorDto(
    int ArticleId,
    string ArticleCode,
    string ArticleLabel,
    int? BomBaseId,
    string? BomCode,
    IReadOnlyList<ArticleBomLineDto> Lines);

public sealed record ArticleBomLineDto(
    int Id,
    int LineNo,
    int ComponentArticleId,
    string ComponentCode,
    string? ComponentLabel,
    double Quantity,
    string Unit,
    double LossRate,
    int? SizeOptionId,
    string? SizeCode,
    int? ColorOptionId,
    string? ColorCode);

public sealed record UpsertArticleBomLineRequest(
    int FinishedGoodArticleId,
    int? LineId,
    int ComponentArticleId,
    double Quantity,
    string Unit,
    int? SizeOptionId = null,
    int? ColorOptionId = null,
    double LossRate = 0);

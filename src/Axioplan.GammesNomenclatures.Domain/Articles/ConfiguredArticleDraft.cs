namespace Axioplan.GammesNomenclatures.Domain.Articles;

public sealed record ConfiguredArticleDraft(
    string Code,
    string Label,
    string SizeTechnicalCode,
    string ColorTechnicalCode,
    string? LanguageTechnicalCode = null);

namespace Axioplan.GammesNomenclatures.Domain.Articles;

/// <summary>
/// Moteur de génération article configuré — exemple vignette composition (cadrage §9).
/// Règle : tailles × couleurs (× langues si génératrice).
/// Codification exacte des préfixes : À confirmer (exemple doc : VC-{commande}-{PF}-{taille}-{couleur}).
/// </summary>
public static class ArticleConfiguratorEngine
{
    public static IReadOnlyList<ConfiguredArticleDraft> GenerateVignetteArticles(
        string customerOrderCode,
        string finishedGoodCode,
        IReadOnlyList<string> sizeTechnicalCodes,
        IReadOnlyList<string> colorTechnicalCodes,
        IReadOnlyList<string>? languageTechnicalCodes = null,
        bool languageIsGenerator = false,
        string codePrefix = "VC")
    {
        if (sizeTechnicalCodes.Count == 0)
        {
            throw new InvalidOperationException("Au moins une taille est requise.");
        }

        if (colorTechnicalCodes.Count == 0)
        {
            throw new InvalidOperationException("Au moins une couleur est requise.");
        }

        var languages = languageIsGenerator && languageTechnicalCodes is { Count: > 0 }
            ? languageTechnicalCodes
            : [string.Empty];

        var drafts = new List<ConfiguredArticleDraft>();

        foreach (var size in sizeTechnicalCodes)
        {
            foreach (var color in colorTechnicalCodes)
            {
                foreach (var language in languages)
                {
                    var code = BuildVignetteCode(codePrefix, customerOrderCode, finishedGoodCode, size, color, language);
                    var label = BuildVignetteLabel(customerOrderCode, finishedGoodCode, size, color, language);
                    drafts.Add(new ConfiguredArticleDraft(
                        code,
                        label,
                        size,
                        color,
                        string.IsNullOrEmpty(language) ? null : language));
                }
            }
        }

        return drafts;
    }

    public static string BuildVignetteCode(
        string prefix,
        string customerOrderCode,
        string finishedGoodCode,
        string sizeTechnicalCode,
        string colorTechnicalCode,
        string? languageTechnicalCode = null)
    {
        var parts = new List<string> { prefix, customerOrderCode, finishedGoodCode, sizeTechnicalCode, colorTechnicalCode };
        if (!string.IsNullOrWhiteSpace(languageTechnicalCode))
        {
            parts.Add(languageTechnicalCode);
        }

        return string.Join('-', parts);
    }

    public static string BuildVignetteLabel(
        string customerOrderCode,
        string finishedGoodCode,
        string sizeTechnicalCode,
        string colorTechnicalCode,
        string? languageTechnicalCode = null)
    {
        var label = $"Vignette {customerOrderCode} {finishedGoodCode} {sizeTechnicalCode} {colorTechnicalCode}";
        if (!string.IsNullOrWhiteSpace(languageTechnicalCode))
        {
            label += $" {languageTechnicalCode}";
        }

        return label.Trim();
    }

    public static int ExpectedArticleCount(
        int sizeCount,
        int colorCount,
        int languageCount,
        bool languageIsGenerator)
    {
        var languages = languageIsGenerator && languageCount > 0 ? languageCount : 1;
        return sizeCount * colorCount * languages;
    }
}

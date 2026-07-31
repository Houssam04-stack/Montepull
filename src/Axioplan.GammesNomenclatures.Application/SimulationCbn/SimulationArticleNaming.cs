namespace Axioplan.GammesNomenclatures.Application.SimulationCbn;

/// <summary>
/// Normalise un nom saisi en code article template (suffixe _BASE).
/// </summary>
public static class SimulationArticleNaming
{
    public static string NormalizeTemplateCode(string articleName)
    {
        if (string.IsNullOrWhiteSpace(articleName))
        {
            throw new ArgumentException("Le nom de l'article est requis.", nameof(articleName));
        }

        var code = articleName.Trim().ToUpperInvariant().Replace(' ', '_');
        if (!code.EndsWith("_BASE", StringComparison.Ordinal))
        {
            code += "_BASE";
        }

        return code;
    }

    public static bool IsPantalonDemoSeed(string articleName)
    {
        var code = NormalizeTemplateCode(articleName);
        return string.Equals(code, "PANTALON_BASE", StringComparison.Ordinal);
    }

    public static bool IsTunimapulfSeed(string articleName)
    {
        var code = NormalizeTemplateCode(articleName);
        return string.Equals(code, "TUNIMAPULF_BASE", StringComparison.Ordinal);
    }

    public const string TunimapulfSimulationName = "TUNIMAPULF SIM";
}

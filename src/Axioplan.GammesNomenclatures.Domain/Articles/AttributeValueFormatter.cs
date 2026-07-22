using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Axioplan.GammesNomenclatures.Domain.Articles;

/// <summary>
/// Nettoyage, formatage et normalisation des valeurs attribut (cadrage article §7.2).
/// La valeur brute ne doit jamais servir directement à la codification.
/// </summary>
public static class AttributeValueFormatter
{
    private static readonly Regex MultipleSpaces = new(@"\s+", RegexOptions.Compiled);

    public static FormattedValue Format(string rawValue, AttributeFormattingRule? rule = null)
    {
        rule ??= new AttributeFormattingRule();

        var working = rawValue ?? string.Empty;
        if (rule.TrimSpaces)
        {
            working = working.Trim();
        }

        if (rule.CollapseSpaces)
        {
            working = MultipleSpaces.Replace(working, " ");
        }

        if (rule.RemoveInternalSpaces)
        {
            working = working.Replace(" ", string.Empty);
        }

        if (!string.IsNullOrEmpty(rule.ForbiddenChars))
        {
            foreach (var forbidden in rule.ForbiddenChars)
            {
                working = working.Replace(forbidden.ToString(), string.Empty);
            }
        }

        var normalized = ApplyCase(working, rule.CaseRule);
        var display = rule.CaseRule switch
        {
            "UPPER" when !rule.RemoveInternalSpaces => ToTitleCase(working),
            "LOWER" => working.ToLowerInvariant(),
            "TITLE" => ToTitleCase(working),
            _ => working,
        };

        if (rule.CaseRule == "UPPER" && rule.RemoveInternalSpaces)
        {
            display = normalized;
        }

        var technicalBase = rule.StripAccentsForCode
            ? RemoveDiacritics(normalized)
            : normalized;

        technicalBase = technicalBase.ToUpperInvariant();

        var technicalCode = rule.RemoveInternalSpaces
            ? technicalBase
            : technicalBase.Replace(' ', '-');

        return new FormattedValue(rawValue ?? string.Empty, display, normalized, technicalCode);
    }

    public static string ApplyCase(string value, string caseRule)
    {
        return caseRule switch
        {
            "UPPER" => value.ToUpperInvariant(),
            "LOWER" => value.ToLowerInvariant(),
            "TITLE" => ToTitleCase(value),
            _ => value,
        };
    }

    public static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
    }

    public static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

namespace Axioplan.GammesNomenclatures.Domain.Articles;

public sealed record AttributeFormattingRule(
    bool TrimSpaces = true,
    bool CollapseSpaces = true,
    bool RemoveInternalSpaces = false,
    string CaseRule = "UPPER",
    bool StripAccentsForCode = true,
    string? ForbiddenChars = null);

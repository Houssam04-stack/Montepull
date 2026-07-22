namespace Axioplan.GammesNomenclatures.Domain.Articles;

public sealed record FormattedValue(
    string RawValue,
    string DisplayValue,
    string NormalizedValue,
    string TechnicalCode);

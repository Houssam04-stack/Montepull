using Axioplan.GammesNomenclatures.Domain;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class FormulaEngineTests
{
    [Fact]
    public void Evaluate_applies_argument_coefficients_with_precedence()
    {
        var coeffs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["SIZE"] = 1.1,
            ["COLOR"] = 1.2,
            ["GAUGE"] = 1.0
        };

        var result = FormulaEngine.Evaluate("BesoinBase * SIZE * COLOR * GAUGE", 0.5, coeffs);

        Assert.Equal(0.66, result, 3);
    }

    [Fact]
    public void Validate_rejects_unknown_token()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SIZE" };

        Assert.Throws<InvalidOperationException>(() => FormulaEngine.Validate("BesoinBase * COLOR", allowed));
    }

    [Fact]
    public void CalculateNetQuantityFromFormula_falls_back_without_expression()
    {
        var coeffs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["SIZE"] = 2,
            ["COLOR"] = 3
        };

        var result = GenerationEngine.CalculateNetQuantityFromFormula(0.5, null, coeffs);

        Assert.Equal(3.0, result, 3);
    }
}

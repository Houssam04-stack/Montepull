using Axioplan.GammesNomenclatures.Application.SimulationCbn;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class SimulationArticleNamingTests
{
    [Theory]
    [InlineData("robe", "ROBE_BASE")]
    [InlineData("ROBE_BASE", "ROBE_BASE")]
    [InlineData("Ma Veste", "MA_VESTE_BASE")]
    [InlineData("PANTALON", "PANTALON_BASE")]
    public void NormalizeTemplateCode_appends_base_suffix(string input, string expected)
    {
        Assert.Equal(expected, SimulationArticleNaming.NormalizeTemplateCode(input));
    }

    [Theory]
    [InlineData("PANTALON", true)]
    [InlineData("pantalon_base", true)]
    [InlineData("ROBE", false)]
    public void IsPantalonDemoSeed_detects_demo_article(string input, bool expected)
    {
        Assert.Equal(expected, SimulationArticleNaming.IsPantalonDemoSeed(input));
    }
}

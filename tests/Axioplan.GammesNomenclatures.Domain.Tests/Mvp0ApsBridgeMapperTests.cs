using Axioplan.GammesNomenclatures.Domain.Mvp0;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class Mvp0ApsBridgeMapperTests
{
    [Theory]
    [InlineData("FINISHED", "FINISHED_GOOD")]
    [InlineData("SEMI", "SEMI_FINISHED")]
    [InlineData("COMPONENT", "COMPONENT")]
    [InlineData(null, "COMPONENT")]
    public void MapArticleType_maps_known_labels(string? input, string expected)
        => Assert.Equal(expected, Mvp0ApsBridgeMapper.MapArticleType(input));

    [Fact]
    public void ResolveOperationCode_picks_last_sequence_on_parent()
    {
        var ops = new List<Mvp0RoutingOpRow>
        {
            new("PF-01", "TRICOT", 10, "CC_TRICOT", 0.1, 0.05, 1),
            new("PF-01", "REMAIL", 20, "CC_REMAIL", 0.2, 0.1, 2)
        };
        Assert.Equal("REMAIL", Mvp0ApsBridgeMapper.ResolveOperationCode("PF-01", ops));
    }
}

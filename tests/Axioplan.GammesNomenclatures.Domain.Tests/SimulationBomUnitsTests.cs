using Axioplan.GammesNomenclatures.Application.SimulationCbn;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class SimulationBomUnitsTests
{
    [Theory]
    [InlineData("PCS", "un")]
    [InlineData("kg", "kg")]
    [InlineData("MIN", "min")]
    public void Normalize_maps_legacy_and_case(string input, string expected)
    {
        Assert.Equal(expected, SimulationBomUnits.Normalize(input));
    }
}

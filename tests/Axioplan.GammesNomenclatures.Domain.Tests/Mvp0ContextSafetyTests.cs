using Axioplan.GammesNomenclatures.Application.Mvp0;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class Mvp0ContextSafetyTests
{
    [Theory]
    [InlineData(null, "SIMULATED")]
    [InlineData("DEMO", "SIMULATED")]
    [InlineData("MONTEPULL_REAL", "REAL")]
    [InlineData("REAL", "REAL")]
    [InlineData("UNKNOWN", "SIMULATED")]
    public void Source_provenance_is_conservative(string? source, string expected)
        => Assert.Equal(expected, Mvp0ContextService.GetProvenance(source));

}

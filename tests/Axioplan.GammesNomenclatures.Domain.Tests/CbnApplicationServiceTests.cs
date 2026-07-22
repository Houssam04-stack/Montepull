using Axioplan.GammesNomenclatures.Application.Cbn;
using Axioplan.GammesNomenclatures.Application.Models;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public class CbnApplicationServiceTests
{
    [Theory]
    [InlineData(CalculationSourceType.Real, 42, "REAL-42")]
    [InlineData(CalculationSourceType.Simulation, 7, "SIMULATION-7")]
    [InlineData(CalculationSourceType.Demo, 1, "DEMO-1")]
    public void FormatRunId_prefixes_source_type_and_numeric_id(
        CalculationSourceType sourceType,
        int runId,
        string expected)
    {
        Assert.Equal(expected, CbnApplicationService.FormatRunId(sourceType, runId));
    }

    [Fact]
    public void CalculationRunContext_real_without_errors_is_validated_for_production_planning()
    {
        var context = new CalculationRunContext
        {
            RunId = "REAL-10",
            SourceType = CalculationSourceType.Real,
            ExistingCbnRunId = 10
        };

        Assert.True(context.IsValidatedForProductionPlanning);
    }

    [Fact]
    public void CalculationRunContext_simulation_is_not_validated_for_production_planning()
    {
        var context = new CalculationRunContext
        {
            RunId = "SIMULATION-3",
            SourceType = CalculationSourceType.Simulation,
            ExistingCbnRunId = 3
        };

        Assert.False(context.IsValidatedForProductionPlanning);
    }
}

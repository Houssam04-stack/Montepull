using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.Expectations;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class ApsCompilerEngineTests
{
    [Fact]
    public void Hash_is_deterministic()
    {
        var a = ApsCompilerHash.Compute("PF1", "SEG_B", "CIR1", "bom", "rtg");
        var b = ApsCompilerHash.Compute("pf1", "seg_b", "cir1", "BOM", "RTG");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Hash_changes_when_source_changes()
    {
        var a = ApsCompilerHash.Compute("PF1", "bom-v1");
        var b = ApsCompilerHash.Compute("PF1", "bom-v2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CompileSkeleton_produces_four_artifact_kinds_payloads()
    {
        var key = new ApsCompileKey("DEMO_PF", ApsSegments.B, "CIR_INT");
        var bundle = ApsCompilerEngine.CompileSkeleton(
            key,
            "bom",
            "rtg",
            "",
            "",
            "",
            "",
            "",
            "",
            [("FIL", 0.2, "TRICOT")],
            [("REMAIL", "REMAILLAGE", 10, true)]);

        Assert.Equal(key, bundle.Key);
        Assert.False(string.IsNullOrWhiteSpace(bundle.SourceHash));
        Assert.Single(bundle.Needs);
        Assert.Contains(bundle.Needs, n => n.ConfirmationStatus == "TO_CONFIRM");
        Assert.Contains(bundle.Hre, h => h.ResourceCode == "REMAILLAGE");
        Assert.NotEmpty(bundle.Loads);
        Assert.NotEmpty(bundle.LeadTimes);
    }

    [Fact]
    public void CompileSkeleton_same_sources_same_hash()
    {
        var key = new ApsCompileKey("DEMO_PF", ApsSegments.A, "C1");
        var b1 = ApsCompilerEngine.CompileSkeleton(key, "b", "r", "c", "y", "s", "e", "u", "d", [], []);
        var b2 = ApsCompilerEngine.CompileSkeleton(key, "b", "r", "c", "y", "s", "e", "u", "d", [], []);
        Assert.Equal(b1.SourceHash, b2.SourceHash);
    }
}

public sealed class ApsExpectationGrainRulesTests
{
    [Theory]
    [InlineData(ApsExpectationEmitters.M2, ApsExpectationGrains.Month, true)]
    [InlineData(ApsExpectationEmitters.M2, ApsExpectationGrains.Sequence, false)]
    [InlineData(ApsExpectationEmitters.M6, ApsExpectationGrains.Sequence, true)]
    [InlineData(ApsExpectationEmitters.M4, ApsExpectationGrains.Week, true)]
    [InlineData(ApsExpectationEmitters.M4, ApsExpectationGrains.Month, false)]
    public void Grain_rules(string emitter, string grain, bool expected)
    {
        Assert.Equal(expected, ApsExpectationGrainRules.IsAllowed(emitter, grain));
    }
}

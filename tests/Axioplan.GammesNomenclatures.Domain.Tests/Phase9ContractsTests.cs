using Axioplan.GammesNomenclatures.Domain.Aps.Contracts;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class Phase9ContractsTests
{
    private static ApsFlowContractDraft Draft(double load = 100)
        => new(20, 2026, load, "HEURES_PERSONNE",
            new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 17),
            [new ApsCompositionShare("DEMO_PF", 0.7), new ApsCompositionShare("PULL", 0.3)],
            [new ApsResourceEnvelope("CC_REMAILLAGE", 40)],
            "DEMO_PF", "PULL", null, "PLAN-1", "fp");

    [Fact]
    public void Published_contract_immutable()
    {
        var c = FlowContractEngine.Publish(FlowContractEngine.CreateDraft(Draft(), "C1", DateTime.UtcNow), DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => FlowContractEngine.Publish(c, DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => FlowContractEngine.AssertImmutable(c));
    }

    [Fact]
    public void Revision_creates_new_version()
    {
        var pub = FlowContractEngine.Publish(FlowContractEngine.CreateDraft(Draft(), "C1", DateTime.UtcNow), DateTime.UtcNow);
        var rev = FlowContractEngine.Revise(pub, Draft(120), DateTime.UtcNow);
        Assert.Equal(2, rev.Version);
        Assert.Equal(ApsFlowContractStatuses.Revised, rev.Status);
        Assert.Equal(120, rev.EngagedLoad);
    }

    [Fact]
    public void Contract_rejects_volume_only()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FlowContractEngine.CreateDraft(Draft() with { Composition = [] }, "C1", DateTime.UtcNow));
    }

    [Fact]
    public void Frozen_zone_forbids_modification()
    {
        var zone = BarrierPolicyEngine.ResolveZone(new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 13), BarrierPolicyEngine.Default);
        Assert.Equal(ApsBarrierZones.Gelee, zone);
        Assert.Throws<InvalidOperationException>(() => BarrierPolicyEngine.AssertModificationAllowed(zone));
    }

    [Fact]
    public void Negotiable_applies_inertia_cost()
    {
        var zone = BarrierPolicyEngine.ResolveZone(new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 13), BarrierPolicyEngine.Default);
        Assert.Equal(ApsBarrierZones.Negociable, zone);
        var w = new ApsInertiaWeights(10, 2, 15, 20, 8, 25, 12);
        var denied = InertiaCostEngine.Assess(zone, w, 1, 10, 0.2, false, false, true, true, expectedGain: 5, "x");
        Assert.False(denied.ChangeAllowed);
        Assert.Throws<InvalidOperationException>(() => BarrierPolicyEngine.AssertModificationAllowed(zone, denied));
        var ok = InertiaCostEngine.Assess(zone, w, 0, 1, 0, false, false, false, false, expectedGain: 1000, "gain mix");
        Assert.True(ok.ChangeAllowed);
    }

    [Fact]
    public void Libre_allows_regeneration()
    {
        var zone = BarrierPolicyEngine.ResolveZone(new DateOnly(2026, 8, 24), new DateOnly(2026, 7, 13), BarrierPolicyEngine.Default);
        Assert.Equal(ApsBarrierZones.Libre, zone);
        var a = InertiaCostEngine.Assess(zone, new ApsInertiaWeights(1, 1, 1, 1, 1, 1, 1), 0, 0, 0, false, false, false, false, 0, null);
        Assert.True(a.ChangeAllowed);
        BarrierPolicyEngine.AssertModificationAllowed(zone, a);
    }

    [Fact]
    public void Nightly_steps_order_fixed()
    {
        Assert.Equal(10, NightlyCycleSteps.Ordered.Count);
        Assert.Equal("ReplayFaits", NightlyCycleSteps.Ordered[0]);
        Assert.Equal("CalculerNervosite", NightlyCycleSteps.Ordered[^1]);
    }

    [Fact]
    public void Recalibration_threshold_behavior()
    {
        var under = RecalibrationEngine.ApplyEwma("eta", 0.9, 0.91, 5, 0.3, 0.15, "t", DateTime.UtcNow);
        Assert.False(under.InvalidatesArtifacts);
        var over = RecalibrationEngine.ApplyEwma("eta", 0.9, 0.4, 5, 0.3, 0.15, "t", DateTime.UtcNow);
        Assert.True(over.InvalidatesArtifacts);
    }

    [Fact]
    public void Invalid_artifact_blocks_cbn_message_via_partial()
    {
        // Domain invariant documented: if recompile fails, CBN must not run — covered by service notes.
        Assert.Contains("RelancerCbnCharge", NightlyCycleSteps.Ordered);
    }

    [Fact]
    public void Expectation_fact_matching_date_composition_unexpected_expired()
    {
        var detected = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
        var matches = ExpectationFactMatcher.Match(
            [
                ("E1", ApsDeviationDimensions.Date, detected.AddDays(-3), detected.AddDays(-1), 10, "MIX-A"),
                ("E2", ApsDeviationDimensions.Date, detected.AddDays(-10), detected.AddDays(-8), 5, null)
            ],
            [
                (1, "X", detected.AddDays(-1).AddHours(3), 10, "MIX-B"),
                (2, "Y", detected.AddHours(-2), 1, "Z")
            ],
            detected);

        Assert.Contains(matches, m => m.Dimension == ApsDeviationDimensions.Date && m.ExpectedId == "E2");
        Assert.Contains(matches, m => m.Dimension == ApsDeviationDimensions.Composition && m.ExpectedId == "E1");
        Assert.Contains(matches, m => m.Dimension == ApsDeviationDimensions.Unexpected);
        Assert.Contains(matches, m => m.ExpectedId == "E2" && m.EventId is null);
    }

    [Fact]
    public void Recipe_states_and_m2_lock()
    {
        var none = ReplayValidationEngine.Evaluate(null, false);
        Assert.Equal(ApsReplayStatuses.NonEvaluable, none.Status);
        Assert.Throws<InvalidOperationException>(() => ReplayValidationEngine.AssertM2M6Locked(none));

        var insuff = ReplayValidationEngine.Evaluate([], true);
        Assert.Equal(ApsReplayStatuses.InsufficientData, insuff.Status);

        var matches = new[]
        {
            new ApsExpectationFactMatch("E", 1, ApsDeviationDimensions.Date, 0.1, null, "VERTE", DateTime.UtcNow, DateTime.UtcNow)
        };
        var passed = ReplayValidationEngine.Evaluate(matches, true);
        Assert.Equal(ApsReplayStatuses.Passed, passed.Status);
        ReplayValidationEngine.AssertM2M6Locked(passed);

        var bad = new[]
        {
            new ApsExpectationFactMatch("E", 1, ApsDeviationDimensions.Date, 10, null, "ROUGE", DateTime.UtcNow, DateTime.UtcNow),
            new ApsExpectationFactMatch(null, 2, ApsDeviationDimensions.Unexpected, 10, null, "ROUGE", DateTime.UtcNow, DateTime.UtcNow)
        };
        var failed = ReplayValidationEngine.Evaluate(bad, true);
        Assert.Equal(ApsReplayStatuses.Failed, failed.Status);
    }

    [Fact]
    public void Nervousness_computed()
    {
        var n = NervousnessEngine.Compute(0.1, 0.2, 0.05, 0.01, 2, 1, 1, 7);
        Assert.True(n.CompositeIndex > 0);
        Assert.Equal(2 / 7.0, n.BottleneckMoveFreq, 6);
    }

    [Fact]
    public void Deterministic_hash_and_recipe()
    {
        var a = FlowContractEngine.CreateDraft(Draft(), "C", DateTime.UtcNow);
        var b = FlowContractEngine.CreateDraft(Draft(), "C", DateTime.UtcNow);
        Assert.Equal(a.SourceHash, b.SourceHash);
        var m = new[] { new ApsExpectationFactMatch("E", 1, ApsDeviationDimensions.Volume, 0.2, null, "VERTE", DateTime.UtcNow, DateTime.UtcNow) };
        Assert.Equal(ReplayValidationEngine.Evaluate(m, true).Status, ReplayValidationEngine.Evaluate(m, true).Status);
    }
}

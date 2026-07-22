using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class FluxPhase7Tests
{
    private static readonly DateOnly D = new(2026, 7, 15);
    private static readonly DateOnly D2 = new(2026, 7, 21);

    [Fact]
    public void Machine_load_in_machine_hours()
    {
        var line = new ApsCompiledLoadLine("CC_TRICOT", ApsResourceAlgebras.Machine, 0.1, 0.5, "HEURES_MACHINE", true, "h1");
        var launch = new ApsLaunchQuantity("PF", "CC_TRICOT", D, 10);
        var r = LoadEngine.ComputeBucket(line, launch, ApsResourceAlgebras.Machine, "HEURES_MACHINE");
        Assert.Equal(1.5, r.Charge, 6); // 10*0.1+0.5
        Assert.Equal("HEURES_MACHINE", r.Unit);
        Assert.Contains(r.Traces, t => t.Contains("MACHINE"));
    }

    [Fact]
    public void Labour_load_in_person_hours()
    {
        var line = new ApsCompiledLoadLine("CC_REMAIL", ApsResourceAlgebras.Labour, 0.2, 0, "HEURES_PERSONNE", true, "h1");
        var launch = new ApsLaunchQuantity("PF", "CC_REMAIL", D, 100);
        var r = LoadEngine.ComputeBucket(line, launch, ApsResourceAlgebras.Labour, "HEURES_PERSONNE");
        Assert.Equal(20, r.Charge, 6);
        Assert.Equal("HEURES_PERSONNE", r.Unit);
    }

    [Fact]
    public void Lot_rounds_up_full_baths_never_hours()
    {
        var line = new ApsCompiledLoadLine("CC_LOT", ApsResourceAlgebras.Lot, 0, 0, "BAINS", true, "h1");
        var launch = new ApsLaunchQuantity("PF", "CC_LOT", D, 75);
        var r = LoadEngine.ComputeBucket(line, launch, ApsResourceAlgebras.Lot, "BAINS", bathMaxCapacity: 40);
        Assert.Equal(2, r.Charge); // ceil(75/40)=2
        Assert.Equal("BAINS", r.Unit);
        Assert.Throws<InvalidOperationException>(() =>
            LoadEngine.ComputeBucket(line with { Unit = "HEURES_MACHINE" }, launch, ApsResourceAlgebras.Lot, "HEURES_MACHINE", 40));
    }

    [Fact]
    public void External_loads_no_internal_capacity_only_milestones()
    {
        var line = new ApsCompiledLoadLine("EXT1", ApsResourceAlgebras.External, 1, 1, "NONE", true, "h1");
        var launch = new ApsLaunchQuantity("PF", "EXT1", D, 50);
        var r = LoadEngine.ComputeBucket(line, launch, ApsResourceAlgebras.External, "NONE");
        Assert.Equal(0, r.Charge);
        Assert.NotNull(r.ExternalMilestones);
        Assert.Contains(r.ExternalMilestones!, m => m.MilestoneType == "REMISE");
        Assert.Contains(r.ExternalMilestones!, m => m.MilestoneType == "RECEPTION");
    }

    [Fact]
    public void Rho_only_on_window_and_cap_zero_handled()
    {
        var ok = SaturationEngine.Compute("R", D, D2, 10, 20, 0.85);
        Assert.Equal(0.5, ok.Rho);
        Assert.Equal(ApsSaturationStatuses.SousCharge, ok.Status);

        var inf = SaturationEngine.Compute("R", D, D2, 5, 0, 0.85);
        Assert.Null(inf.Rho);
        Assert.Equal(ApsSaturationStatuses.SaturationInfinie, inf.Status);

        var nc = SaturationEngine.Compute("R", D, D2, 0, 0, 0.85);
        Assert.Equal(ApsSaturationStatuses.NonCalculable, nc.Status);
    }

    [Fact]
    public void Bottleneck_is_argmax_rho_or_yarn_if_blocking()
    {
        var sats = new[]
        {
            SaturationEngine.Compute("A", D, D2, 5, 10, 0.85),
            SaturationEngine.Compute("B", D, D2, 9, 10, 0.85)
        };
        var bn = BottleneckEngine.Identify(D, D2, sats);
        Assert.Equal("B", bn.ConstrainingCode);

        var yarn = BottleneckEngine.Identify(D, D2, sats, new ApsMaterialConstraint("FIL", true, 20, "ATP insuffisant"));
        Assert.Equal("FIL", yarn.ConstrainingCode);
        Assert.Equal("MATERIAL", yarn.Kind);
    }

    [Fact]
    public void Bottleneck_moved_only_on_real_change_with_previous()
    {
        var a = new ApsBottleneckResult("A", "RESOURCE", 0.9, D, D2, "x", 1, 1, null, [], "a");
        var b = new ApsBottleneckResult("B", "RESOURCE", 0.95, D, D2, "x", 1, 1, null, [], "b");
        Assert.False(BottleneckEngine.ShouldJournalBottleneckMoved(null, a, true));
        Assert.False(BottleneckEngine.ShouldJournalBottleneckMoved(a, a, true));
        Assert.False(BottleneckEngine.ShouldJournalBottleneckMoved(a, b, false));
        Assert.True(BottleneckEngine.ShouldJournalBottleneckMoved(a, b, true));
    }

    [Fact]
    public void Buffer_in_hre_coverage_and_adequacy_without_mix()
    {
        var stocks = new[]
        {
            new ApsBufferStockLine("P1", "F", "M", null, 100, 0.2, "AMONT"),
            new ApsBufferStockLine("P2", "F", "M", null, 50, 0.1, "AVAL")
        };
        var snap = BufferEngine.ComputeUpstreamBuffer("CC_REMAIL", stocks, dailyEngageableCapacityRemail: 5, expectedMix: null);
        Assert.Equal(20, snap.BufferHre, 6); // 100*0.2 only AMONT
        Assert.Equal(4, snap.CoverageDays); // 20/5
        Assert.Equal("TO_CONFIRM", snap.AdequacyStatus);
        Assert.Null(snap.Adequacy);
    }

    [Fact]
    public void Space_rho_independent_of_capacity_rho()
    {
        var space = SpaceSaturationEngine.Compute("Z1", 90, 100);
        Assert.Equal(0.9, space.RhoEspace);
        Assert.Contains("rho_espace", space.Trace);
        Assert.Contains("≠ rho_capacite", space.Trace);
    }

    [Fact]
    public void Rope_does_not_create_work_order()
    {
        var rope = RopeEngine.RecommendKnittingLaunch(10, 30, 20, new Dictionary<string, double> { ["P1"] = 1 }, false);
        Assert.Equal(20, rope.RecommendedQty); // 10 + (30-20)
        Assert.False(rope.CreatesWorkOrder);
    }

    [Fact]
    public void Invalid_charges_artifact_refused()
    {
        var line = new ApsCompiledLoadLine("R", ApsResourceAlgebras.Machine, 1, 0, "HEURES_MACHINE", false, "bad");
        Assert.Throws<InvalidOperationException>(() =>
            LoadEngine.ComputeBucket(line, new ApsLaunchQuantity("A", "R", D, 1), ApsResourceAlgebras.Machine, "HEURES_MACHINE"));
    }

    [Fact]
    public void Deterministic_saturation_and_buffer_target_zones()
    {
        var a = SaturationEngine.Compute("R", D, D2, 10, 10, 0.85);
        var b = SaturationEngine.Compute("R", D, D2, 10, 10, 0.85);
        Assert.Equal(a.Rho, b.Rho);
        Assert.Equal(a.Status, b.Status);

        var target = BufferEngine.ComputeTarget(10, k: 2, upstreamVariances: [16, 9], reactionLeadDays: 1);
        // 2*sqrt(25)+1 = 11
        Assert.Equal(11, target.BufferTargetHre);
        Assert.Equal(ApsBufferZones.Verte, target.Zone); // actual 10 almost target, consumed small
    }

    [Fact]
    public void Unit_mix_forbidden()
    {
        var line = new ApsCompiledLoadLine("R", ApsResourceAlgebras.Machine, 1, 0, "HEURES_MACHINE", true, "h");
        Assert.Throws<InvalidOperationException>(() =>
            LoadEngine.ComputeBucket(line, new ApsLaunchQuantity("A", "R", D, 1), ApsResourceAlgebras.Machine, "HEURES_PERSONNE"));
    }
}

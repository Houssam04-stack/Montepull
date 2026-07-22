using Axioplan.GammesNomenclatures.Domain.Aps.Capacity;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class NetCapacityEngineTests
{
    [Fact]
    public void Five_stages_and_reserve_deduction()
    {
        var bucket = new ApsCapacityBucket(DateOnly.FromDateTime(DateTime.Today), "JOUR", 8, true);
        var result = NetCapacityEngine.ComputeBucket(
            "CC_REMAIL", "MAIN_OEUVRE", ApsCapacityUnits.LabourHours,
            bucket, ResourceCount: 1, [], eta: 0.9, rhoTarget: 0.85, alreadyReserved: 0);

        Assert.Equal(8, result.CapBrute);
        Assert.Equal(8, result.CapOuverte);
        Assert.Equal(7.2, result.CapEffective, 3);
        Assert.Equal(7.2 * 0.15, result.Reserve, 3);
        Assert.Equal(result.CapEffective - result.Reserve, result.CapNette, 6);
        Assert.Equal(5, result.Traces.Count);
    }

    [Fact]
    public void Already_reserved_deducted_from_engageable()
    {
        var bucket = new ApsCapacityBucket(DateOnly.FromDateTime(DateTime.Today), null, 8, true);
        var result = NetCapacityEngine.ComputeBucket(
            "CC", "MACHINE", ApsCapacityUnits.MachineHours,
            bucket, 1, [], 1, 1, alreadyReserved: 2);
        Assert.Equal(6, result.CapEngageable);
    }

    [Fact]
    public void Lot_never_in_hours()
    {
        var bucket = new ApsCapacityBucket(DateOnly.FromDateTime(DateTime.Today), null, 8, true);
        Assert.Throws<InvalidOperationException>(() =>
            NetCapacityEngine.ComputeBucket("LOT1", "LOT", ApsCapacityUnits.MachineHours, bucket, 10, [], 1, 0.8, 0));

        var ok = NetCapacityEngine.ComputeBucket("LOT1", "LOT", ApsCapacityUnits.Baths, bucket, 96, [], 1, 0.8, 0);
        Assert.Equal(96, ok.CapBrute);
        Assert.Equal(ApsCapacityUnits.Baths, ok.Unit);
    }

    [Fact]
    public void Tier_not_available_before_activation_lead()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var buckets = new[]
        {
            NetCapacityEngine.ComputeBucket("R", "MACHINE", ApsCapacityUnits.MachineHours,
                new ApsCapacityBucket(today, null, 8, true), 1, [], 1, 1, 0)
        };
        var tiers = new[]
        {
            new ApsElasticityTier("ST", "R", ActivationLeadDays: 45, Gain: 10, TierEta: 1, null, ApsCapacityUnits.MachineHours, "OK")
        };
        var cum = NetCapacityEngine.ComputeCapCum("R", ApsCapacityUnits.MachineHours, today, today.AddDays(7), today, buckets, tiers);
        Assert.False(cum.Tiers[0].Activable);
        Assert.Equal(0, cum.Tiers[0].EffectiveGain);
    }

    [Fact]
    public void External_without_engagement_is_zero()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var qty = NetCapacityEngine.ComputeExternalAvailability([], "SEG_B", today, today.AddDays(30));
        Assert.Equal(0, qty);
    }

    [Fact]
    public void Deterministic_same_inputs_same_outputs()
    {
        var d = DateOnly.FromDateTime(DateTime.Today);
        var b = new ApsCapacityBucket(d, "JOUR", 8, true);
        var a = NetCapacityEngine.ComputeBucket("R", "MACHINE", ApsCapacityUnits.MachineHours, b, 2, [], 0.9, 0.85, 1);
        var c = NetCapacityEngine.ComputeBucket("R", "MACHINE", ApsCapacityUnits.MachineHours, b, 2, [], 0.9, 0.85, 1);
        Assert.Equal(a.CapEngageable, c.CapEngageable);
        Assert.Equal(a.Traces.Count, c.Traces.Count);
    }
}

public sealed class SegmentCbnAndYarnAtpTests
{
    [Fact]
    public void Yarn_atp_uses_max_not_sum()
    {
        var baths = new[]
        {
            new ApsYarnBathStock("BAIN-A", "FIL", 40, "KG", "LIBRE"),
            new ApsYarnBathStock("BAIN-B", "FIL", 35, "KG", "LIBRE")
        };
        var atp = YarnBathAvailabilityEngine.Evaluate(baths, 60, ApsBathCompatRules.Piece);
        Assert.Equal(40, atp.Available);
        Assert.Equal("BAIN-A", atp.SelectedBathCode);
        Assert.Equal(20, atp.Shortage);
        Assert.DoesNotContain("75", atp.Justification.Split('(')[0]); // MAX not sum as result
        Assert.Contains("MAX", atp.Justification);
        Assert.Contains("75", atp.Justification); // mentions forbidden sum
    }

    [Fact]
    public void Invalid_artifact_blocks_cbn()
    {
        var demand = new ApsSegmentDemand("D1", "PF", 10, DateOnly.FromDateTime(DateTime.Today), ApsBathCompatRules.Piece, false, "CMD1");
        var result = SegmentCbnEngine.Run(demand, "SEG_A", false, "hash", [], []);
        Assert.False(result.Success);
        Assert.Contains("INVALID", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reserve_mts_not_consumed_silently_by_mto()
    {
        var demand = new ApsSegmentDemand("D1", "PF", 10, DateOnly.FromDateTime(DateTime.Today), ApsBathCompatRules.Piece, false, "CMD1");
        var needs = new[] { new ApsCompiledNeed("FIL", 1, 0, "LIBRE", "PF>FIL") };
        var stocks = new[]
        {
            new ApsStockPosition("FIL", "RESERVE_MTS", 100, "BAIN-MTS", null),
            new ApsStockPosition("FIL", "LIBRE", 5, "BAIN-A", null)
        };
        var result = SegmentCbnEngine.Run(demand, "SEG_A", true, "h", needs, stocks);
        Assert.True(result.Success);
        var line = Assert.Single(result.Lines);
        Assert.Equal(5, line.Available); // only LIBRE bath, not MTS 100
        Assert.Contains(line.Traces, t => t.Contains("RESERVE_MTS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Yield_not_applied_twice_uses_artifact_quantity_only()
    {
        var demand = new ApsSegmentDemand("D1", "PF", 10, DateOnly.FromDateTime(DateTime.Today), ApsBathCompatRules.Piece, false, null);
        // artefact deja avec rendement : 0.2
        var needs = new[] { new ApsCompiledNeed("FIL", 0.2, 0, "LIBRE", "PF>FIL") };
        var stocks = Array.Empty<ApsStockPosition>();
        var result = SegmentCbnEngine.Run(demand, "SEG_B", true, "h", needs, stocks);
        Assert.Equal(2.0, result.Lines[0].GrossNeed, 6);
        Assert.Contains(result.Lines[0].Traces, t => t.Contains("non reapplique rendement"));
    }

    [Fact]
    public void Segment_netting_independent_per_compiled_component()
    {
        var demand = new ApsSegmentDemand("D1", "PF", 10, DateOnly.FromDateTime(DateTime.Today), ApsBathCompatRules.Piece, true, null);
        var needs = new[] { new ApsCompiledNeed("PIECE-DEMO", 1, 0, "LIBRE", "PF>PIECE") };
        var stocks = new[] { new ApsStockPosition("PIECE-DEMO", "LIBRE", 3, null, null) };
        var result = SegmentCbnEngine.Run(demand, "SEG_B", true, "h", needs, stocks, lotMultiple: 1);
        Assert.Equal(7, result.Lines[0].NetNeed);
        Assert.Contains(result.Lines[0].Traces, t => t.Contains("plein droit"));
    }

    [Fact]
    public void Netting_stops_at_decoupling_point_does_not_cross_segments()
    {
        // SEG_B ne voit que les besoins compiles piece — stock FIL ignore (point de decouplage).
        var demand = new ApsSegmentDemand("D1", "PF", 10, DateOnly.FromDateTime(DateTime.Today), ApsBathCompatRules.Piece, false, "CMD1");
        var needs = new[] { new ApsCompiledNeed("PIECE-DEMO", 1, 0, "LIBRE", "PF>PIECE") };
        var stocks = new[]
        {
            new ApsStockPosition("PIECE-DEMO", "LIBRE", 2, null, null),
            new ApsStockPosition("FIL-DEMO", "LIBRE", 1000, "BAIN-A", null)
        };
        var result = SegmentCbnEngine.Run(demand, "SEG_B", true, "h", needs, stocks, lotMultiple: 1);
        Assert.True(result.Success);
        Assert.Single(result.Lines);
        Assert.Equal("PIECE-DEMO", result.Lines[0].ArticleCode);
        Assert.Equal(2, result.Lines[0].Available);
        Assert.Equal(8, result.Lines[0].NetNeed);
        Assert.DoesNotContain(result.Lines, l => l.ArticleCode.Contains("FIL"));
    }
}

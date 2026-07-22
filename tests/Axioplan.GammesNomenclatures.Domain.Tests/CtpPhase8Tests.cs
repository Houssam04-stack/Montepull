using System.Diagnostics;
using Axioplan.GammesNomenclatures.Domain.Aps.Ctp;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class CtpPhase8Tests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    private static ApsCtpDemand Demand(
        double qty = 50,
        DateOnly? desired = null,
        DateOnly? max = null,
        string regime = "MTO",
        string customer = "CLI-STD",
        double? margin = 1000)
        => new("DEM-T1", "DEMO_PF", customer, "PULL", "STD", qty,
            desired ?? Today.AddDays(14), max ?? Today.AddDays(40),
            margin, 5, regime, null, "ORD-1");

    private static ApsCtpEvaluationContext Ctx(
        ApsCtpDemand demand,
        string route,
        double pf,
        ApsCtpYarnState yarn,
        double capDay = 10,
        double loaded = 0,
        double load = 1,
        double? hre = 0.15,
        string hreStatus = "OK",
        double? sigma = 0.05,
        IReadOnlyList<ApsCtpExternalSlot>? ext = null)
    {
        var days = Enumerable.Range(0, 46)
            .Select(i => new ApsCtpResourceDay("CC_REMAILLAGE", Today.AddDays(i), capDay, 0.85, loaded))
            .ToList();
        return new ApsCtpEvaluationContext(
            demand, demand.DeclaredRegime ?? "MTO", route, pf, yarn, days, ext ?? [],
            "CIR_REMAIL_INT", "CIR_ALT", "CC_REMAILLAGE", load, hre, hreStatus, sigma,
            Today, 45, new ApsFloatThresholds());
    }

    private static ApsCtpYarnState YarnOk(double qty = 80)
        => new(true, "BAIN-A", qty, Today, false, null, null, 0);

    private static ApsCtpYarnState YarnPurchase(double lead = 21, double? mu = 21, double? sig = 5)
        => new(false, null, 0, Today.AddDays((int)lead), true, mu, sig, lead);

    [Fact]
    public void Same_input_same_output()
    {
        var d = Demand();
        var ctx = Ctx(d, "R2", 0, YarnOk());
        var a = CtpEngine.Evaluate(ctx, false);
        var b = CtpEngine.Evaluate(ctx, false);
        Assert.Equal(a.Outcome, b.Outcome);
        Assert.Equal(a.ProposedDate, b.ProposedDate);
        Assert.Equal(a.Bottleneck, b.Bottleneck);
        Assert.Equal(a.Hre, b.Hre);
        Assert.Equal(a.FloatDays, b.FloatDays);
    }

    [Fact]
    public void R1_reserves_flag_and_high_reliability()
    {
        var d = Demand(regime: "MTS", customer: "CLI-MTS");
        var ctx = Ctx(d, "R1", pf: 200, YarnOk(0));
        var a = CtpEngine.Evaluate(ctx, commitReservations: true);
        Assert.Equal(ApsCtpOutcomes.FeasibleInternal, a.Outcome);
        Assert.Equal(ApsRegimes.Mts, a.EffectiveRegime);
        Assert.True(a.ReservesCreated);
        Assert.Equal(0.99, a.Reliability.Value);
        Assert.Equal("STOCK_PF", a.Bottleneck);
    }

    [Fact]
    public void R1_insufficient_routes_to_mto_via_router()
    {
        var route = CtpRouterEngine.Route("MTS", pfAvailableLibre: 10, demandQty: 50, yarnBathAvailable: true, yarnPurchaseRequired: false);
        Assert.Equal("R2", route.Route);
        Assert.Contains(route.Traces, t => t.Contains("bascule MTO"));
    }

    [Fact]
    public void Yarn_uses_compatible_bath_code()
    {
        var d = Demand();
        var a = CtpEngine.Evaluate(Ctx(d, "R2", 0, YarnOk()), false);
        Assert.Equal("BAIN-A", a.SelectedBath);
    }

    [Fact]
    public void Reserve_mts_not_silently_in_router_without_pf()
    {
        // MTS with 0 PF -> R2, does not invent MTS promise
        var route = CtpRouterEngine.Route("MTS", 0, 50, true, false);
        Assert.Equal("R2", route.Route);
        var a = CtpEngine.Evaluate(Ctx(Demand(regime: "MTO"), "R2", 0, YarnOk()), false);
        Assert.NotEqual(ApsRegimes.Mts, a.EffectiveRegime);
    }

    [Fact]
    public void R3_respects_supplier_lead()
    {
        var d = Demand(desired: Today.AddDays(5), max: Today.AddDays(40));
        var yarn = YarnPurchase(21);
        var a = CtpEngine.Evaluate(Ctx(d, "R3", 0, yarn), false);
        Assert.True(a.ProposedDate is null || a.ProposedDate >= Today.AddDays(21));
        if (a.ProposedDate is not null)
        {
            Assert.True(a.ProposedDate >= Today.AddDays(21));
        }
    }

    [Fact]
    public void No_external_without_engagement()
    {
        var d = Demand();
        // Cap faible pour forcer échec interne, sans externe
        var a = CtpEngine.Evaluate(Ctx(d, "R2", 0, YarnOk(), capDay: 0.01, loaded: 0.01, load: 5, ext: []), false);
        Assert.Equal(ApsCtpOutcomes.Infeasible, a.Outcome);
        Assert.Null(a.ExternalEngagementCode);
        Assert.Contains(a.OptionsTried, o => o.Contains("EXTERNE_ABSENT") || o.Contains("EXTERNE_"));
    }

    [Fact]
    public void First_feasible_date_by_iteration()
    {
        var d = Demand(desired: Today, max: Today.AddDays(30));
        // loaded high so early days fail rho
        var a = CtpEngine.Evaluate(Ctx(d, "R2", 0, YarnOk(), capDay: 5, loaded: 4, load: 1), false);
        Assert.NotNull(a.ProposedDate);
        Assert.True(a.ProposedDate >= Today.AddDays(3));
        Assert.True(a.HorizonChronology.Count > 0);
        Assert.Contains(a.HorizonChronology, h => !h.Passed);
        Assert.Contains(a.HorizonChronology, h => h.Passed);
    }

    [Fact]
    public void Evaluation_does_not_set_reserves()
    {
        var a = CtpEngine.Evaluate(Ctx(Demand(), "R2", 0, YarnOk()), commitReservations: false);
        Assert.False(a.ReservesCreated);
    }

    [Fact]
    public void Promise_flag_sets_reserves()
    {
        var a = CtpEngine.Evaluate(Ctx(Demand(), "R2", 0, YarnOk()), commitReservations: true);
        Assert.True(a.ReservesCreated);
    }

    [Fact]
    public void Bottleneck_explained_structured()
    {
        var a = CtpEngine.Evaluate(Ctx(Demand(), "R2", 0, YarnOk()), false);
        Assert.False(string.IsNullOrWhiteSpace(a.Cause.Explanation));
        Assert.False(string.IsNullOrWhiteSpace(a.Cause.ConstraintType));
    }

    [Fact]
    public void Reliability_to_confirm_without_estimators()
    {
        var r = ReliabilityEngine.ForR2Internal(null);
        Assert.Equal("TO_CONFIRM", r.Status);
        Assert.Null(r.Value);
        var r3 = ReliabilityEngine.ForR3Supplier(null, null);
        Assert.Equal("TO_CONFIRM", r3.Status);
    }

    [Fact]
    public void Hre_from_context_and_density_non_calculable_if_absent()
    {
        var ok = CtpEngine.Evaluate(Ctx(Demand(), "R2", 0, YarnOk(), hre: 0.2, hreStatus: "OK"), false);
        Assert.Equal(50 * 0.2, ok.Hre);
        Assert.Equal("OK", ok.DensityStatus);

        var bad = CtpEngine.Evaluate(Ctx(Demand(), "R2", 0, YarnOk(), hre: null, hreStatus: "TO_CONFIRM"), false);
        Assert.Equal("NON_CALCULABLE", bad.DensityStatus);
        Assert.Null(bad.MarginDensity);
    }

    [Fact]
    public void Float_classification_configurable()
    {
        Assert.Equal(ApsFloatStatuses.Rigide, CtpEngine.ClassifyFloat(0, new ApsFloatThresholds()));
        Assert.Equal(ApsFloatStatuses.Faible, CtpEngine.ClassifyFloat(2, new ApsFloatThresholds()));
        Assert.Equal(ApsFloatStatuses.Moyen, CtpEngine.ClassifyFloat(8, new ApsFloatThresholds()));
        Assert.Equal(ApsFloatStatuses.Eleve, CtpEngine.ClassifyFloat(20, new ApsFloatThresholds()));
    }

    [Fact]
    public void Infeasible_has_m2_recommendation()
    {
        var a = CtpEngine.Evaluate(Ctx(Demand(), "R2", 0, YarnOk(), capDay: 0.01, loaded: 0.01, load: 10), false);
        Assert.Equal(ApsCtpOutcomes.Infeasible, a.Outcome);
        Assert.NotNull(a.EscalateToM2Recommendation);
    }

    [Fact]
    public void Subcontract_when_engagement_compatible()
    {
        var ext = new[]
        {
            new ApsCtpExternalSlot("ST_REMAIL_01", "ST_REMAIL_01", 370,
                Today.AddDays(5), Today.AddDays(20), Today.AddDays(30), "OPEN", 0.9, 0.05)
        };
        var a = CtpEngine.Evaluate(Ctx(Demand(), "R2", 0, YarnOk(), capDay: 0.01, loaded: 0.01, load: 10, ext: ext), false);
        Assert.Equal(ApsCtpOutcomes.FeasibleSubcontract, a.Outcome);
        Assert.Equal("ST_REMAIL_01", a.ExternalEngagementCode);
    }

    [Fact]
    public void Regime_specificity_article_client_wins()
    {
        var rules = new[]
        {
            new ApsRegimeDefaultRule("PULL", "STD", null, null, null, null, "MTO", 1),
            new ApsRegimeDefaultRule(null, null, "DEMO_PF", "CLI-MTS", null, null, "MTS", 4)
        };
        var d = Demand(regime: null!, customer: "CLI-MTS") with { DeclaredRegime = null };
        var r = RegimeResolutionEngine.Resolve(d, rules, Today);
        Assert.Equal("MTS", r);
    }

    [Fact]
    public void Budget_under_2_seconds_mvp()
    {
        var d = Demand();
        var ctx = Ctx(d, "R2", 0, YarnOk());
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            _ = CtpEngine.Evaluate(ctx, false);
        }

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Elapsed {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Unit_mix_not_applicable_but_hre_zero_density_blocked()
    {
        var a = CtpEngine.Evaluate(Ctx(Demand(), "R1", 200, YarnOk(0), hre: 0, hreStatus: "OK"), true);
        Assert.Equal("NON_CALCULABLE", a.DensityStatus);
    }
}

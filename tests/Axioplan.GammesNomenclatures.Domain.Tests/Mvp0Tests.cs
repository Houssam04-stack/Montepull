using Axioplan.GammesNomenclatures.Domain.Mvp0;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class Mvp0Tests
{
    [Fact]
    public void Campaign_carries_single_family_concept()
    {
        var c = new Mvp0Campaign(Guid.NewGuid(), "C1", "PULL", "S1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1),
            "P", Mvp0CampaignStatuses.Draft, "DEMO", Mvp0Provenance.Simulated, DateTime.UtcNow, 0, 85, 70, null);
        Assert.Equal("PULL", c.FamilyCode);
    }

    [Fact]
    public void Import_reject_never_silent()
    {
        var (ok, rejects) = Mvp0ImportGuard.Partition<string>([
            (null, "inconnu", 3),
            ("A", null, 4)
        ]);
        Assert.Single(ok);
        Assert.Contains(rejects, r => r.Contains("Ligne 3"));
    }

    [Fact]
    public void Duplicate_article_detected()
    {
        var rows = new[]
        {
            new Mvp0ArticleRow("A1", "FINISHED", "UN", "PULL", true, Mvp0Provenance.Real, 1),
            new Mvp0ArticleRow("A1", "FINISHED", "UN", "PULL", true, Mvp0Provenance.Real, 2)
        };
        var a = Mvp0ValidationEngine.ValidateArticles(rows, "PULL");
        Assert.Contains(a, x => x.RuleCode == "ART_CODE_UNIQUE");
    }

    [Fact]
    public void Bom_cycle_detected()
    {
        var articles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P", "C" };
        var boms = new[]
        {
            new Mvp0BomRow("P", "C", 1, "UN", 0.01, 1),
            new Mvp0BomRow("C", "P", 1, "UN", 0.01, 2)
        };
        var a = Mvp0ValidationEngine.ValidateBoms(boms, articles, false);
        Assert.Contains(a, x => x.RuleCode == "BOM_CYCLE");
        Assert.Equal(Mvp0Severities.Blocking, a.First(x => x.RuleCode == "BOM_CYCLE").Severity);
    }

    [Fact]
    public void Routing_cycle_and_unknown_center_and_negative_time()
    {
        var arts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P" };
        var centers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CC1" };
        var ops = new[]
        {
            new Mvp0RoutingOpRow("P", "OP1", 10, "CCX", -1, 0, 1),
            new Mvp0RoutingOpRow("P", "OP1", 20, "CC1", 0.2, 0, 2)
        };
        var a = Mvp0ValidationEngine.ValidateRoutings(ops, arts, centers, 0.01, 8);
        Assert.Contains(a, x => x.RuleCode == "RTG_CENTER_UNKNOWN");
        Assert.Contains(a, x => x.RuleCode == "RTG_CYCLE_NEGATIVE");
        Assert.Contains(a, x => x.RuleCode == "RTG_OP_CYCLE");
    }

    [Fact]
    public void Time_out_of_range_is_warning()
    {
        var arts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P" };
        var centers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CC1" };
        var ops = new[] { new Mvp0RoutingOpRow("P", "OP", 10, "CC1", 50, 1, 1) };
        var a = Mvp0ValidationEngine.ValidateRoutings(ops, arts, centers, 0.01, 8);
        Assert.Contains(a, x => x.RuleCode == "RTG_CYCLE_PLAUSIBILITY" && x.Severity == Mvp0Severities.Warning);
    }

    [Fact]
    public void Bom_op_link_missing_detected()
    {
        var arts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P", "C" };
        var boms = new[] { new Mvp0BomRow("P", "C", 1, "UN", 0.01, 1) };
        var a = Mvp0ValidationEngine.ValidateBoms(boms, arts, true, new HashSet<string>());
        Assert.Contains(a, x => x.RuleCode == "BOM_OP_LINK_MISSING");
    }

    [Fact]
    public void Bypass_requires_justification_and_keeps_anomaly()
    {
        Assert.Throws<InvalidOperationException>(() => Mvp0BypassRules.Create("A1", "X", " ", "bob", "HIGH", 1));
        var bp = Mvp0BypassRules.Create("A1", "X", "raison", "bob", "HIGH", 2);
        Assert.Equal("ACTIVE", bp.Status);
        var anom = new Mvp0Anomaly("A1", "BOM_CYCLE", Mvp0Severities.Blocking, "BOM", "X", null, null, "S", 1, "m", "r", "OPEN", DateTime.UtcNow);
        Assert.Equal("OPEN", anom.Status); // bypass does not delete anomaly
    }

    [Fact]
    public void Input_score_deterministic_and_explicable()
    {
        var weights = new Mvp0ReliabilityWeights(15, 15, 15, 10, 8, 5, 8, 14, 10, 10, 10, 10, 1);
        var checks = new Dictionary<string, int>
        {
            ["Articles"] = 10, ["Nomenclatures"] = 10, ["Gammes"] = 10, ["Temps"] = 5,
            ["Calendriers"] = 5, ["TRS"] = 5, ["LiaisonBOM-OP"] = 5, ["HistoriqueOF"] = 5
        };
        var a = new[]
        {
            new Mvp0Anomaly("1", "ART_CODE_UNIQUE", Mvp0Severities.Blocking, "ARTICLE", "A", null, null, "S", 1, "m", "r", "OPEN", DateTime.UtcNow)
        };
        var s1 = Mvp0DataReliabilityEngine.Compute(a, [], weights, checks, DateTime.UtcNow, DateTime.UtcNow, 30, 90);
        var s2 = Mvp0DataReliabilityEngine.Compute(a, [], weights, checks, DateTime.UtcNow, DateTime.UtcNow, 30, 90);
        Assert.Equal(s1.GlobalScore, s2.GlobalScore);
        Assert.Contains("Σ", s1.Explanation);
        Assert.NotEmpty(s1.Domains);
    }

    [Fact]
    public void Freshness_bands()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(Mvp0FreshnessBands.Fresh, Mvp0FreshnessEngine.Band(now.AddDays(-1), now, 30, 90));
        Assert.Equal(Mvp0FreshnessBands.Aging, Mvp0FreshnessEngine.Band(now.AddDays(-40), now, 30, 90));
        Assert.Equal(Mvp0FreshnessBands.Stale, Mvp0FreshnessEngine.Band(now.AddDays(-120), now, 30, 90));
        Assert.Equal(Mvp0FreshnessBands.Unknown, Mvp0FreshnessEngine.Band(null, now, 30, 90));
    }

    [Fact]
    public void Backtest_without_solver_computes_gaps()
    {
        var today = new DateOnly(2026, 7, 1);
        var wos = new[]
        {
            new Mvp0WorkOrderActual("OF1", "A", today, today.AddDays(2), 100, 95, 5, 10, 12, Mvp0Provenance.Real)
        };
        var bt = Mvp0BacktestReliabilityEngine.Run(wos, 3, 0.25);
        Assert.Equal(2, bt.MeanDateErrorDays);
        Assert.Equal(2, bt.Items[0].DurationErrorHours);
        Assert.True(bt.MeanQtyRelError > 0);
        var score = Mvp0BacktestReliabilityEngine.Score(bt);
        Assert.True(score.GlobalScore >= 0);
        var score2 = Mvp0BacktestReliabilityEngine.Score(bt);
        Assert.Equal(score.GlobalScore, score2.GlobalScore);
    }

    [Fact]
    public void Simulated_data_forbids_go()
    {
        var input = new Mvp0InputReliabilityResult(90, [], "GO_CANDIDATE", "x");
        var result = new Mvp0ResultReliabilityResult(90, 90, 90, 90, 90, 90, 1, "x");
        var gate = Mvp0GateDecisionEngine.Decide(input, result, [], [], true, anySimulatedData: true, 85, 70);
        Assert.Equal(Mvp0GateOutcomes.InsufficientData, gate.Outcome);
    }

    [Fact]
    public void Blocking_forbids_go_and_planner_required()
    {
        var input = new Mvp0InputReliabilityResult(90, [], "x", "x");
        var result = new Mvp0ResultReliabilityResult(90, 90, 90, 90, 90, 90, 1, "x");
        var anom = new Mvp0Anomaly("A", "BOM_CYCLE", Mvp0Severities.Blocking, "BOM", "X", null, null, "S", 1, "m", "r", "OPEN", DateTime.UtcNow);
        var noPlanner = Mvp0GateDecisionEngine.Decide(input, result, [anom], [], false, false, 85, 70);
        Assert.Equal(Mvp0GateOutcomes.InsufficientData, noPlanner.Outcome);
        var blocked = Mvp0GateDecisionEngine.Decide(input, result, [anom], [], true, false, 85, 70);
        Assert.Equal(Mvp0GateOutcomes.NoGo, blocked.Outcome);
    }

    [Fact]
    public void Go_with_reservations_possible()
    {
        var input = new Mvp0InputReliabilityResult(75, [], "x", "x");
        var result = new Mvp0ResultReliabilityResult(72, 70, 70, 70, 70, 70, 1, "x");
        var bp = Mvp0BypassRules.Create("A", "X", "ok", "p", "LOW", 1);
        var gate = Mvp0GateDecisionEngine.Decide(input, result, [], [bp], true, false, 85, 70);
        Assert.Equal(Mvp0GateOutcomes.GoWithReservations, gate.Outcome);
    }

    [Fact]
    public void Csv_reject_blank_never_silent_and_auto_map()
    {
        var csv = "code;type;unit;family\nA1;FINISHED;UN;PULL\n;;;\n";
        var preview = Mvp0CsvImportEngine.Preview(Mvp0DataTypes.Articles, csv);
        Assert.Empty(preview.RequiredMissing);
        var (report, payload) = Mvp0CsvImportEngine.Parse(Mvp0DataTypes.Articles, csv, null, Mvp0Provenance.Real);
        Assert.True(report.LinesRejected >= 1);
        Assert.Contains(report.Rejects, r => r.Contains("Ligne"));
        Assert.IsType<List<Mvp0ArticleRow>>(payload);
    }

    [Fact]
    public void Pure_go_when_thresholds_met_no_bypass()
    {
        var input = new Mvp0InputReliabilityResult(90, [], "x", "x");
        var result = new Mvp0ResultReliabilityResult(90, 90, 90, 90, 90, 90, 1, "x");
        var gate = Mvp0GateDecisionEngine.Decide(input, result, [], [], true, false, 85, 70);
        Assert.Equal(Mvp0GateOutcomes.Go, gate.Outcome);
    }
}

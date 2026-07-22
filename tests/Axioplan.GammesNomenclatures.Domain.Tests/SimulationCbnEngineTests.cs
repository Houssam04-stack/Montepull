using Axioplan.GammesNomenclatures.Domain.Simulation;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class SimulationCbnEngineTests
{
    [Fact]
    public void Duplication_applies_size_coefficient_and_color_substitution_for_pantalon_noir_l()
    {
        var tissuBase = 10;
        var tissuNoir = 11;
        var fil = 12;
        var bouton = 13;
        var zip = 14;
        var emballage = 15;

        var bom = new List<SimulationDuplicationEngine.TemplateBomLine>
        {
            new(1, tissuBase, "TISSU_BASE", 1.20, 0, 0, true, true),
            new(1, fil, "FIL", 0.05, 0, 0, true, false),
            new(1, bouton, "BOUTON", 1, 0, 0, false, false),
            new(1, zip, "ZIP", 1, 0, 0, false, false),
            new(1, emballage, "EMBALLAGE", 1, 0, 0, false, false)
        };

        var routing = new List<SimulationDuplicationEngine.TemplateRoutingOp>
        {
            new(1, 10, "Coupe tissu", "ATELIER", 0, 8, 0, 0, true, false),
            new(1, 20, "Couture", "ATELIER", 0, 20, 0, 0, true, false),
            new(1, 30, "Montage zip", "ATELIER", 0, 5, 0, 0, false, false)
        };

        var substitutions = new List<SimulationDuplicationEngine.SubstitutionRule>
        {
            new(tissuBase, "COLOR", "Noir", tissuNoir)
        };

        var codes = new Dictionary<int, string>
        {
            [tissuBase] = "TISSU_BASE",
            [tissuNoir] = "TISSU_NOIR",
            [fil] = "FIL",
            [bouton] = "BOUTON",
            [zip] = "ZIP",
            [emballage] = "EMBALLAGE"
        };

        var variant = SimulationDuplicationEngine.GenerateVariant(
            "PANTALON_BASE",
            "Pantalon base",
            "L",
            "Noir",
            1.15,
            1.0,
            bom,
            routing,
            substitutions,
            codes);

        Assert.Equal("PANTALON_NOIR_L", variant.Code);
        Assert.Equal(1.38, variant.BomLines.First(l => l.ComponentCode == "TISSU_NOIR").QuantityPer, 3);
        Assert.Equal(0.0575, variant.BomLines.First(l => l.ComponentCode == "FIL").QuantityPer, 4);
        Assert.Equal(1.0, variant.BomLines.First(l => l.ComponentCode == "BOUTON").QuantityPer, 3);
        Assert.Equal(9.2, variant.RoutingOps.First(o => o.OperationNumber == 10).RunTimeMinutes, 3);
        Assert.Equal(23.0, variant.RoutingOps.First(o => o.OperationNumber == 20).RunTimeMinutes, 3);
        Assert.Equal(5.0, variant.RoutingOps.First(o => o.OperationNumber == 30).RunTimeMinutes, 3);
    }

    [Fact]
    public void Cbn_run_produces_of_for_fg_and_oa_for_purchased_components()
    {
        // PANTALON_NOIR_L (1) components: TISSU_NOIR(2), FIL(3), BOUTON(4), ZIP(5), EMBALLAGE(6)
        var articles = new List<SimArticleInput>
        {
            new(1, "PANTALON_NOIR_L", "Pantalon", SimulationArticleTypes.FinishedGood, SimulationProcurementTypes.Manufactured, 5, "L", "Noir"),
            new(2, "TISSU_NOIR", "Tissu", SimulationArticleTypes.RawMaterial, SimulationProcurementTypes.Purchased, 4, null, null),
            new(3, "FIL", "Fil", SimulationArticleTypes.Purchased, SimulationProcurementTypes.Purchased, 2, null, null),
            new(4, "BOUTON", "Bouton", SimulationArticleTypes.Purchased, SimulationProcurementTypes.Purchased, 2, null, null),
            new(5, "ZIP", "Zip", SimulationArticleTypes.Purchased, SimulationProcurementTypes.Purchased, 3, null, null),
            new(6, "EMBALLAGE", "Emballage", SimulationArticleTypes.Purchased, SimulationProcurementTypes.Purchased, 1, null, null)
        };

        var bom = new List<SimBomLineInput>
        {
            new(1, 1, 2, 1.38, 0, 0),
            new(2, 1, 3, 0.0575, 0, 0),
            new(3, 1, 4, 1, 0, 0),
            new(4, 1, 5, 1, 0, 0),
            new(5, 1, 6, 1, 0, 0)
        };

        var parameters = new Dictionary<int, SimCbnParameterInput>
        {
            [1] = new(1, 0, 0, 0, 0, 0, 5, SimulationLotRules.LotForLot, 0, 1),
            [2] = new(2, 50, 10, 0, 0, 20, 4, SimulationLotRules.LotForLot, 0, 1),
            [3] = new(3, 2, 1, 0, 0, 0, 2, SimulationLotRules.LotForLot, 0, 1),
            [4] = new(4, 200, 50, 0, 0, 0, 2, SimulationLotRules.LotForLot, 0, 1),
            [5] = new(5, 70, 20, 0, 0, 0, 3, SimulationLotRules.LotForLot, 0, 1),
            [6] = new(6, 100, 20, 0, 0, 0, 1, SimulationLotRules.LotForLot, 0, 1)
        };

        var order = new SimSalesOrderInput(10, "CMD001", 1, 100, new DateOnly(2026, 7, 30));
        var result = SimulationCbnEngine.Run(order, articles, bom, parameters, new DateOnly(2026, 7, 1));

        Assert.Equal(0, result.LowLevelCodes[1]);
        Assert.Equal(1, result.LowLevelCodes[2]);
        Assert.Contains(result.FlatBomLines, f => f.ComponentArticleCode == "TISSU_NOIR" && Math.Abs(f.CumulativeQuantity - 1.38) < 0.0001);

        // FG net = 100
        var fgNet = result.NetRequirements.First(n => n.ArticleCode == "PANTALON_NOIR_L");
        Assert.Equal(100, fgNet.NetRequirement, 3);

        // Tissu: gross 138, available = 50+20-10 = 60 => net 78
        var tissuNet = result.NetRequirements.First(n => n.ArticleCode == "TISSU_NOIR");
        Assert.Equal(138, tissuNet.GrossRequirement, 3);
        Assert.Equal(78, tissuNet.NetRequirement, 3);

        Assert.Contains(result.PlannedOrders, p => p.ArticleCode == "PANTALON_NOIR_L" && p.OrderType == "OF");
        Assert.Contains(result.PlannedOrders, p => p.ArticleCode == "TISSU_NOIR" && p.OrderType == "OA");

        var fgOrder = result.PlannedOrders.First(p => p.ArticleCode == "PANTALON_NOIR_L");
        Assert.Contains("a fabriquer", fgOrder.Justification);
        Assert.Contains("Manufactured", fgOrder.Justification);

        var tissuOrder = result.PlannedOrders.First(p => p.ArticleCode == "TISSU_NOIR");
        Assert.Contains("a acheter", tissuOrder.Justification);
        Assert.Contains("Purchased", tissuOrder.Justification);
        Assert.Contains("78", tissuOrder.Justification);

        Assert.Contains(result.WorkOrders, w => w.ArticleCode == "PANTALON_NOIR_L");
        Assert.NotEmpty(result.PeggingTree);
        Assert.StartsWith("CMD001", result.PeggingTree[0].Path);
    }

    [Fact]
    public void DetectCycle_returns_true_on_loop()
    {
        var bom = new List<SimBomLineInput>
        {
            new(1, 1, 2, 1, 0, 0),
            new(2, 2, 1, 1, 0, 0)
        };

        Assert.True(SimulationCbnEngine.DetectCycle(1, bom, out var path));
        Assert.Contains("1", path);
    }

    [Fact]
    public void Net_requirement_formula_floors_at_zero()
    {
        var param = new SimCbnParameterInput(1, 100, 0, 0, 0, 0, 1, SimulationLotRules.LotForLot, 0, 1);
        Assert.Equal(0, SimulationCbnEngine.CalculateNetRequirement(10, param));
        Assert.Equal(40, SimulationCbnEngine.CalculateNetRequirement(50, new SimCbnParameterInput(1, 10, 0, 0, 0, 0, 1, SimulationLotRules.LotForLot, 0, 1)));
    }
}

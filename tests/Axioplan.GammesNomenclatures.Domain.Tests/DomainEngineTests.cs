using Axioplan.GammesNomenclatures.Domain;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public class BomFlattenerTests
{
    [Fact]
    public void Flatten_multi_level_pf_sf_component()
    {
        var root = new[]
        {
            new BomFlattener.BomLineInput(10, 2, "PANNEAU-SF", "Panneau SF", "SEMI_FINISHED", 1.0, "PIECE", 0, "FIXED"),
            new BomFlattener.BomLineInput(20, 3, "VCOMP-STD", "Vignette", "COMPONENT", 1.0, "PIECE", 0, "FIXED"),
        };

        var subBom = new Dictionary<int, IReadOnlyList<BomFlattener.BomLineInput>>
        {
            [2] = new[]
            {
                new BomFlattener.BomLineInput(10, 4, "FIL-MINT", "Fil mint", "COMPONENT", 0.5, "KG", 0.05, "CALCULATED"),
            },
        };

        var flat = BomFlattener.Flatten(root, id => subBom.TryGetValue(id, out var lines) ? lines : null, 5);

        Assert.Equal(3, flat.Count);
        Assert.Contains(flat, l => l.ComponentCode == "FIL-MINT" && l.BomLevel == 2);
        Assert.Equal(0.5, flat.First(l => l.ComponentCode == "FIL-MINT").QuantityPerUnit, 3);
    }
}

public class PeggingEngineTests
{
    [Fact]
    public void BuildLinks_creates_ov_and_oa_links()
    {
        var needs = new[]
        {
            new PeggingEngine.MaterialNeed(1, 100, 4, "FIL-MINT", "COMPONENT", 15.0, "KG", "AH25PLUMIERE-M-MINT"),
        };

        var sales = new[]
        {
            new PeggingEngine.SupplyCandidate("SALES_ORDER_LINE", 100, "OV-001 L10", 1, 30, "PIECE"),
        };

        var purchases = new[]
        {
            new PeggingEngine.SupplyCandidate("PURCHASE_ORDER_LINE", 50, "OA-001 L10", 4, 20, "KG"),
        };

        var links = PeggingEngine.BuildLinks(needs, sales, [], purchases, [], true);

        Assert.Contains(links, l => l.LinkType == "OV_TO_NEED" && l.Direction == "DOWNSTREAM");
        Assert.Contains(links, l => l.LinkType == "NEED_TO_OA" && l.Direction == "DOWNSTREAM");
    }

    [Fact]
    public void BuildLinks_prioritizes_stock_before_purchase()
    {
        var needs = new[]
        {
            new PeggingEngine.MaterialNeed(1, 100, 4, "FIL-MINT", "COMPONENT", 15.0, "KG", "AH25PLUMIERE-M-MINT"),
        };

        var sales = new[]
        {
            new PeggingEngine.SupplyCandidate("SALES_ORDER_LINE", 100, "OV-001 L10", 1, 30, "PIECE"),
        };

        var purchases = new[]
        {
            new PeggingEngine.SupplyCandidate("PURCHASE_ORDER_LINE", 50, "OA-001 L10", 4, 20, "KG"),
        };

        var stock = new[]
        {
            new PeggingEngine.SupplyCandidate("STOCK_BALANCE", 70, "FIL-MINT", 4, 5, "KG"),
        };

        var links = PeggingEngine.BuildLinks(needs, sales, [], purchases, stock, true);

        Assert.Contains(links, l => l.LinkType == "NEED_TO_STOCK" && l.Quantity == 5);
        Assert.Contains(links, l => l.LinkType == "NEED_TO_OA" && l.Quantity == 10);
    }
}

public class CbnEngineTests
{
    [Fact]
    public void ComputeRequirement_calculates_variable_material_with_coefficients()
    {
        var line = new CbnEngine.FlattenedBomLine(10, "FIL-MINT", 0.5, "KG", 0.05, "CALCULATED");

        var draft = CbnEngine.ComputeRequirement(line, 30, "AH25PLUMIERE-M-MINT", 1.0, 1.0);

        Assert.Equal("VARIABLE_MATERIAL", draft.CalculationMode);
        Assert.Equal(15.0, draft.QuantityNet, 3);
        Assert.Equal(15.789473684210526d, draft.QuantityGross, 3);
    }

    [Fact]
    public void ComputeRequirement_calculates_fixed_supply_per_piece()
    {
        var line = new CbnEngine.FlattenedBomLine(20, "SACHET", 1.0, "PIECE", 0, "FIXED");

        var draft = CbnEngine.ComputeRequirement(line, 30, "AH25PLUMIERE-M-MINT");

        Assert.Equal("FIXED_SUPPLY", draft.CalculationMode);
        Assert.Equal(30.0, draft.QuantityNet, 3);
        Assert.Equal(30.0, draft.QuantityGross, 3);
    }

    [Fact]
    public void ComputeRequirement_marks_unclear_line_as_to_confirm()
    {
        var line = new CbnEngine.FlattenedBomLine(30, "BOUTON", 1.0, "PIECE", 0, "COPY_TO_VALIDATE");

        var draft = CbnEngine.ComputeRequirement(line, 30, "AH25PLUMIERE-M-MINT");

        Assert.Equal("TO_CONFIRM", draft.CalculationMode);
        Assert.Contains("A confirmer", draft.Trace.Formula);
        Assert.Equal(30.0, draft.QuantityNet, 3);
    }
}

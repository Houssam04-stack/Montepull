using Axioplan.GammesNomenclatures.Domain;
using Xunit;
namespace Axioplan.GammesNomenclatures.Domain.Tests;
public class PeggingSupplyConservationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Shared_supplies_are_consumed_once_across_demands(bool bidirectional)
    {
        var needs = new[] {
            new PeggingEngine.MaterialNeed(1, null, 7, "SF", "SEMI_FINISHED", 8, "PIECE", "S"),
            new PeggingEngine.MaterialNeed(2, null, 7, "SF", "SEMI_FINISHED", 12, "PIECE", "M"),
            new PeggingEngine.MaterialNeed(3, null, 7, "SF", "SEMI_FINISHED", 10, "PIECE", "L") };
        var stock = new[] { new PeggingEngine.SupplyCandidate("STOCK_BALANCE", 1, "STOCK", 7, 10, "PIECE") };
        var of = new[] { new PeggingEngine.SupplyCandidate("MANUFACTURING_ORDER", 1, "OF", 7, 5, "PIECE") };
        var oa = new[] { new PeggingEngine.SupplyCandidate("PURCHASE_ORDER_LINE", 1, "OA", 7, 7, "PIECE") };
        var links = PeggingEngine.BuildLinks(needs, [], of, oa, stock, bidirectional)
            .Where(l => l.Direction == "DOWNSTREAM").ToList();
        Assert.Equal(10, links.Where(l => l.TargetEntityType == "STOCK_BALANCE").Sum(l => l.Quantity));
        Assert.Equal(5, links.Where(l => l.TargetEntityType == "MANUFACTURING_ORDER").Sum(l => l.Quantity));
        Assert.Equal(7, links.Where(l => l.TargetEntityType == "PURCHASE_ORDER_LINE").Sum(l => l.Quantity));
        Assert.Equal(8, links.Where(l => l.MaterialRequirementId == 1 && l.LinkType == "NEED_TO_STOCK").Sum(l => l.Quantity));
        Assert.Equal(2, links.Where(l => l.MaterialRequirementId == 2 && l.LinkType == "NEED_TO_STOCK").Sum(l => l.Quantity));
        Assert.Equal(2, links.Where(l => l.MaterialRequirementId == 3).Sum(l => l.Quantity));
        Assert.Equal(8, needs.Sum(n => n.QuantityGross) - links.Sum(l => l.Quantity));
    }
}

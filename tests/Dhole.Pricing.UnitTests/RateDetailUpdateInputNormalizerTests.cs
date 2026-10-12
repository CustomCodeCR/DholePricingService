using Dhole.Pricing.Application.Features.Rates.UpdateRate;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateDetailUpdateInputNormalizerTests
{
    [TestMethod]
    public void TwoDifferentHblCostsSharingOneId_AreBothKept_AndPersistedMatchKeepsId()
    {
        var detailId = Guid.NewGuid();
        var firstCostId = Guid.NewGuid();
        var savedCostId = Guid.NewGuid();
        var first = Hbl(detailId, firstCostId);
        var second = Hbl(detailId, savedCostId);

        var result = RateDetailUpdateInputNormalizer.Normalize(
            [first, second],
            new Dictionary<Guid, Guid?> { [detailId] = savedCostId });

        Assert.AreEqual(2, result.Count);
        Assert.IsNull(result[0].Id);
        Assert.AreEqual(firstCostId, result[0].CostId);
        Assert.AreEqual(detailId, result[1].Id);
        Assert.AreEqual(savedCostId, result[1].CostId);
    }

    [TestMethod]
    public void RetransmissionOfExactlySameDetail_DoesNotCreateADuplicateCharge()
    {
        var detailId = Guid.NewGuid();
        var costId = Guid.NewGuid();
        var row = Hbl(detailId, costId);

        var result = RateDetailUpdateInputNormalizer.Normalize(
            [row, row],
            new Dictionary<Guid, Guid?> { [detailId] = costId });

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(detailId, result[0].Id);
    }

    [TestMethod]
    public void TwoManualDetailsSharingIdWithDifferentSales_KeepBoth()
    {
        var detailId = Guid.NewGuid();
        var first = Hbl(detailId, null);
        var second = first with { SaleAmount = 125m };

        var result = RateDetailUpdateInputNormalizer.Normalize(
            [first, second],
            new Dictionary<Guid, Guid?> { [detailId] = null });

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(detailId, result[0].Id);
        Assert.IsNull(result[1].Id);
        Assert.AreEqual(125m, result[1].SaleAmount);
    }

    private static UpsertRateExtraDetailCommandItem Hbl(Guid id, Guid? costId) => new(
        Id: id,
        CostId: costId,
        Name: "HBL",
        CostDetailType: CostDetailType.DestinationCharge,
        CostType: CostType.Fixed,
        CurrencyId: Guid.NewGuid(),
        CurrencyName: "USD",
        CurrencyCode: "USD",
        CostAmount: 55m,
        SaleAmount: 0m,
        Notes: null,
        Quantity: 1m,
        ChargeBasis: ChargeBasis.PerContainer);
}

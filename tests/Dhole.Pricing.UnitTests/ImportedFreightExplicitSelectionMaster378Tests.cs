using Dhole.Pricing.Application.Features.Rates.CreateRate;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ImportedFreightExplicitSelectionMaster378Tests
{
    [TestMethod]
    public void SourceWithoutChosenFreight_DoesNotAutoInvoice()
    {
        Assert.IsFalse(ImportedFreightExplicitSelectionPolicy.ShouldInclude(Guid.NewGuid(), null));
        Assert.IsFalse(ImportedFreightExplicitSelectionPolicy.ShouldInclude(null, null));
    }

    [TestMethod]
    public void SelectedFreight_IsAllowed()
    {
        var freight = new CreateRateDetailCommandItem(
            null, "Flete", CostDetailType.Freight, CostType.Variable,
            Guid.NewGuid(), "USD", "USD", 5500m, 5500m, null, 1m, null);
        Assert.IsTrue(ImportedFreightExplicitSelectionPolicy.ShouldInclude(Guid.NewGuid(), freight));
        Assert.IsFalse(ImportedFreightExplicitSelectionPolicy.ShouldInclude(null, freight));
    }

    [TestMethod]
    public void HblIsNotFreight()
    {
        var hbl = new CreateRateDetailCommandItem(
            null, "HBL", CostDetailType.DestinationCharge, CostType.Fixed,
            Guid.NewGuid(), "USD", "USD", 0m, 50m, null, 1m, null);
        Assert.IsFalse(ImportedFreightExplicitSelectionPolicy.ShouldInclude(Guid.NewGuid(), hbl));
    }
}

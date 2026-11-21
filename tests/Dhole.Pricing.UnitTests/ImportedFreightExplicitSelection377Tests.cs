using Dhole.Pricing.Application.Features.Rates.CreateRate;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ImportedFreightExplicitSelection377Tests
{
    [TestMethod]
    public void SourceRateWithoutSelectedFreight_NeverAddsOceanFreight()
    {
        Assert.IsFalse(ImportedFreightExplicitSelectionPolicy.ShouldInclude(Guid.NewGuid(), null));
        Assert.IsFalse(ImportedFreightExplicitSelectionPolicy.ShouldInclude(null, null));
    }

    [TestMethod]
    public void ExplicitFreightWithLinkedSource_AddsOceanFreight()
    {
        var requested = new CreateRateDetailCommandItem(
            null, "Flete internacional", CostDetailType.Freight, CostType.Variable,
            Guid.NewGuid(), "USD", "USD", 5500m, 5800m, null, 1m, null);
        Assert.IsTrue(ImportedFreightExplicitSelectionPolicy.ShouldInclude(Guid.NewGuid(), requested));
        Assert.IsFalse(ImportedFreightExplicitSelectionPolicy.ShouldInclude(null, requested));
    }

    [TestMethod]
    public void DestinationChargeDoesNotImplyFreightEvenWhenSourceSelected()
    {
        var other = new CreateRateDetailCommandItem(
            null, "HBL", CostDetailType.DestinationCharge, CostType.Fixed,
            Guid.NewGuid(), "USD", "USD", 0m, 50m, null, 1m, null);
        Assert.IsFalse(ImportedFreightExplicitSelectionPolicy.ShouldInclude(Guid.NewGuid(), other));
    }
}

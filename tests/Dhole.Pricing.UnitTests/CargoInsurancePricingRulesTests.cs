using Dhole.Pricing.Application.Services;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class CargoInsurancePricingRulesTests
{
    [TestMethod]
    public void Calculate_AppliesMinimums_WhenPercentagesAreBelowMinimums()
    {
        var result = CargoInsurancePricingRules.Calculate(10_000m);

        Assert.AreEqual(35m, result.CostAmount);
        Assert.AreEqual(95m, result.SaleAmount);
    }

    [TestMethod]
    public void Calculate_AppliesPercentages_WhenTheyExceedMinimums()
    {
        var result = CargoInsurancePricingRules.Calculate(20_000m);

        Assert.AreEqual(40m, result.CostAmount);
        Assert.AreEqual(130m, result.SaleAmount);
    }

    [TestMethod]
    public void Calculate_RespectsEditableSalePercentageAndMinimum()
    {
        var result = CargoInsurancePricingRules.Calculate(
            cargoValue: 20_000m,
            salePercentage: 0.75m,
            saleMinimumAmount: 175m
        );

        Assert.AreEqual(40m, result.CostAmount);
        Assert.AreEqual(175m, result.SaleAmount);
    }

    [TestMethod]
    public void Calculate_RespectsManualSaleAmountWithoutChangingInsuranceCost()
    {
        var result = CargoInsurancePricingRules.Calculate(
            cargoValue: 20_000m,
            salePercentage: 0.65m,
            saleMinimumAmount: 95m,
            manualSaleAmount: 160m
        );

        Assert.AreEqual(40m, result.CostAmount);
        Assert.AreEqual(160m, result.SaleAmount);
    }
}

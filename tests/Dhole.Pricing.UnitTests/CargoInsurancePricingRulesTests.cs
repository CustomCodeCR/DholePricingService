using Dhole.Pricing.Application.Services;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class CargoInsurancePricingRulesTests
{
    [TestMethod]
    public void Calculate_AppliesCommercialMinimum_WhenPercentageIsBelowMinimum()
    {
        var result = CargoInsurancePricingRules.Calculate(10_000m);

        Assert.AreEqual(35m, result.CostAmount);
        Assert.AreEqual(125m, result.SaleAmount);
    }

    [TestMethod]
    public void Calculate_UsesFobPlusFreightAndOneHundredTenPercentInsuredValue()
    {
        var result = CargoInsurancePricingRules.Calculate(
            cargoValue: 20_000m,
            freightAmount: 2_000m
        );

        // Base asegurada: (20,000 + 2,000) x 110% = 24,200.
        Assert.AreEqual(48.40m, result.CostAmount);
        Assert.AreEqual(205.70m, result.SaleAmount);
    }

    [TestMethod]
    public void Calculate_RespectsEditableSalePercentageAndMinimum()
    {
        var result = CargoInsurancePricingRules.Calculate(
            cargoValue: 20_000m,
            salePercentage: 0.75m,
            saleMinimumAmount: 175m
        );

        Assert.AreEqual(44m, result.CostAmount);
        Assert.AreEqual(175m, result.SaleAmount);
    }

    [TestMethod]
    public void Calculate_RespectsManualSaleAmountWithoutChangingInsuranceCost()
    {
        var result = CargoInsurancePricingRules.Calculate(
            cargoValue: 20_000m,
            salePercentage: 0.65m,
            saleMinimumAmount: 125m,
            manualSaleAmount: 160m
        );

        Assert.AreEqual(44m, result.CostAmount);
        Assert.AreEqual(160m, result.SaleAmount);
    }
}

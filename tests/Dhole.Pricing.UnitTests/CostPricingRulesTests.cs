using Dhole.Pricing.Domain.Costs.Entities;
using System.Text.Json;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class CostPricingRulesTests
{
    [TestMethod]
    public void WeightBands_RespectInclusiveBoundaries_AndResolveIndependentAmount()
    {
        var rules = CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("WeightKg", 1m, 500m, null, 50m, 70m),
            new CostPricingRule("WeightKg", 500.01m, 1500m, null, 90m, 125m),
        }));

        Assert.IsTrue(CostPricingRules.TryResolve(rules, 500m, null, null, 0m, 0m, out var cost, out var sale));
        Assert.AreEqual(50m, cost);
        Assert.AreEqual(70m, sale);
        Assert.IsTrue(CostPricingRules.TryResolve(rules, 500.01m, null, null, 0m, 0m, out cost, out sale));
        Assert.AreEqual(90m, cost);
        Assert.AreEqual(125m, sale);
        Assert.IsFalse(CostPricingRules.TryResolve(rules, 1501m, null, null, 0m, 0m, out _, out _));
    }

    [TestMethod]
    public void PackageBands_UseExactIntegerRanges()
    {
        var rules = CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("Packages", 1m, 3m, null, 20m, 30m),
            new CostPricingRule("Packages", 4m, 15m, null, 45m, 65m),
        }));

        Assert.IsTrue(CostPricingRules.TryResolve(rules, null, 3, null, 0m, 0m, out var cost, out _));
        Assert.AreEqual(20m, cost);
        Assert.IsTrue(CostPricingRules.TryResolve(rules, null, 4, null, 0m, 0m, out cost, out _));
        Assert.AreEqual(45m, cost);
    }

    [TestMethod]
    public void ContainerType_ResolvesOnlyMatchingEquipment()
    {
        var container20 = Guid.NewGuid();
        var container40 = Guid.NewGuid();
        var rules = CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("ContainerType", null, null, container20, 100m, 135m),
            new CostPricingRule("ContainerType", null, null, container40, 160m, 210m),
        }));
        Assert.IsTrue(CostPricingRules.TryResolve(rules, null, null, container40, 0m, 0m, out var cost, out var sale));
        Assert.AreEqual(160m, cost);
        Assert.AreEqual(210m, sale);
        Assert.IsFalse(CostPricingRules.TryResolve(rules, null, null, Guid.NewGuid(), 0m, 0m, out _, out _));
    }

    [TestMethod]
    public void OverlappingBands_AreRejected()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("WeightKg", 1m, 500m, null, 1m, 1m),
            new CostPricingRule("WeightKg", 500m, 1000m, null, 2m, 2m),
        });
        Assert.ThrowsException<InvalidOperationException>(() => CostPricingRules.Normalize(json));
    }

    [TestMethod]
    public void ExistingFixedCost_RetainsLegacyPrices()
    {
        Assert.IsTrue(CostPricingRules.TryResolve(null, null, null, null, 25m, 40m, out var cost, out var sale));
        Assert.AreEqual(25m, cost);
        Assert.AreEqual(40m, sale);
    }

    [TestMethod]
    public void UnavailableCargoDimension_DoesNotSelectAnyBand()
    {
        var json = CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("WeightKg", 1m, 500m, null, 50m, 70m)
        }));
        Assert.IsFalse(CostPricingRules.TryResolve(json, null, null, null, 0m, 0m, out _, out _));
    }
}

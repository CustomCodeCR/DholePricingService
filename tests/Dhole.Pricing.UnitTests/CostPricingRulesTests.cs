using Dhole.Pricing.Domain.Costs.Entities;
using System.Text.Json;
using Dhole.Pricing.Application.Features.Costs.GetCostsForSelect;
using Dhole.Pricing.Contracts.Costs.Response;

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
    public void UserConfiguredFlatAndPerKgBands_AreResolvedWithoutSeededTariffs()
    {
        var rules = CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("WeightKg", 1m, 10m, null, 9m, 12m),
            new CostPricingRule("WeightKg", 11m, 20m, null, 3m, 4m, "PerKg"),
            new CostPricingRule("WeightKg", 21m, null, null, 2m, 5m, "PerKg"),
        }));

        Assert.IsTrue(CostPricingRules.TryResolve(rules, 10m, null, null, 0m, 0m, out var cost, out var sale));
        Assert.AreEqual(9m, cost);
        Assert.AreEqual(12m, sale);
        Assert.IsTrue(CostPricingRules.TryResolve(rules, 15m, null, null, 0m, 0m, out cost, out sale));
        Assert.AreEqual(45m, cost);
        Assert.AreEqual(60m, sale);
    }

    [TestMethod]
    public void PerKgMode_WithPackagesDimensionIsRejected()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("Packages", 1m, 10m, null, 1m, 2m, "PerKg")
        });
        Assert.ThrowsExactly<InvalidOperationException>(() => CostPricingRules.Normalize(json));
    }

    [TestMethod]
    public void FiscalStorage_ExtraDayChargeUsesOnlyUserProvidedDays()
    {
        var rules = CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("Packages", 1m, 2m, null, 2m, 3m, "PerExtraDay", 3),
        }));

        Assert.IsFalse(CostPricingRules.TryResolve(rules, null, 2, null, 0m, 0m, out _, out _, storageDays: 3));
        Assert.IsFalse(CostPricingRules.TryResolve(rules, null, 2, null, 0m, 0m, out _, out _, storageDays: null));
        Assert.IsTrue(CostPricingRules.TryResolve(rules, null, 2, null, 0m, 0m, out var cost, out var sale, storageDays: 5));
        Assert.AreEqual(4m, cost);
        Assert.AreEqual(6m, sale);
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
        Assert.ThrowsExactly<InvalidOperationException>(() => CostPricingRules.Normalize(json));
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
    private static CostSelectDto WizardMaster(string? json) => new(
        Id: Guid.NewGuid(), Name: "Servicio de agencia", CostType: "Fixed",
        CostDetailType: "DestinationCharge",
        CarrierId: null, CarrierName: null, CarrierCode: null,
        AgentId: null, AgentName: null, AgentCode: null,
        PortId: null, PortName: null, PortCode: null, PortRole: null,
        CurrencyId: Guid.NewGuid(), CurrencyName: "USD", CurrencyCode: "USD",
        CostAmount: 0m, SaleAmount: 0m, UtilityAmount: 0m,
        Notes: null, IsAccountant: false,
        Incoterms: Array.Empty<CostIncotermDto>(), PricingRulesJson: json);

    [TestMethod]
    public void WizardBundle_Uses40HcAmounts_AndPreservesRawCatalogSnapshot()
    {
        var dv20 = Guid.NewGuid();
        var hc40 = Guid.NewGuid();
        var master = WizardMaster(CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("ContainerType", null, null, dv20, 175m, 175m),
            new CostPricingRule("ContainerType", null, null, hc40, 350m, 350m)
        })));
        var actual = CostWizardTierProjection.ResolveApplicable([master], null, null, hc40, null).Single();
        Assert.AreEqual(350m, actual.CostAmount);
        Assert.AreEqual(350m, actual.SaleAmount);
        Assert.AreEqual(0m, master.CostAmount);
        Assert.AreEqual(0, CostWizardTierProjection.ResolveApplicable(
            [master], null, null, Guid.NewGuid(), null).Count);
        Assert.AreEqual(0, CostWizardTierProjection.ResolveApplicable(
            [master], null, null, null, null).Count);
        // An absent cargo context is not a valid 0/0 tariff. The catalog
        // separately retains the full unpriced definition and its bands.
    }

    [TestMethod]
    public void WizardBundle_PackagesWeightAndExtraDays_ResolveFromConfiguredBands()
    {
        var weight = WizardMaster(CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("WeightKg", 1m, 25m, null, 4m, 5m, "PerKg")
        })));
        var packages = WizardMaster(CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("Packages", 1m, 3m, null, 20m, 30m),
            new CostPricingRule("Packages", 4m, 12m, null, 80m, 95m)
        })));
        var days = WizardMaster(CostPricingRules.Normalize(JsonSerializer.Serialize(new[]
        {
            new CostPricingRule("Packages", 1m, 12m, null, 8m, 12m, "PerExtraDay", 3)
        })));
        Assert.AreEqual(60m, CostWizardTierProjection.ResolveApplicable(
            [weight], 15m, null, null, null).Single().CostAmount);
        Assert.AreEqual(80m, CostWizardTierProjection.ResolveApplicable(
            [packages], null, 4, null, null).Single().CostAmount);
        Assert.AreEqual(32m, CostWizardTierProjection.ResolveApplicable(
            [days], null, 3, null, 7).Single().CostAmount);
        Assert.AreEqual(0, CostWizardTierProjection.ResolveApplicable(
            [days], null, 3, null, 3).Count);
    }

}

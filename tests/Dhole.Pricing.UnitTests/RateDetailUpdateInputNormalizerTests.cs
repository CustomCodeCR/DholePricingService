using Dhole.Pricing.Application.Features.Rates.UpdateRate;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ExwPickupPricePolicyTests
{
    [TestMethod]
    public void NewExwFixedPickup_KeepsAuthorizedManualCostInsteadOfCatalogDefault()
    {
        var resolved = Dhole.Pricing.Application.Services.ExwPickupPricePolicy.ResolveFixedCostAmount(
            requestedAmount: 110m,
            catalogAmount: 60m,
            updatingPersistedDetail: false,
            selectedCurrencyDiffers: false,
            notes: "Recolecta EXW: precio autorizado en Pricing");
        Assert.AreEqual(110m, resolved);
    }

    [TestMethod]
    public void OtherNewFixedCosts_StillUseCatalogCost()
    {
        var resolved = Dhole.Pricing.Application.Services.ExwPickupPricePolicy.ResolveFixedCostAmount(
            requestedAmount: 110m,
            catalogAmount: 60m,
            updatingPersistedDetail: false,
            selectedCurrencyDiffers: false,
            notes: "Manejo documental");
        Assert.AreEqual(60m, resolved);
    }

    [TestMethod]
    public void ExistingFixedCostAndCurrencyOverride_KeepCurrentBehavior()
    {
        Assert.AreEqual(110m, Dhole.Pricing.Application.Services.ExwPickupPricePolicy.ResolveFixedCostAmount(
            110m, 60m, true, false, null));
        Assert.AreEqual(110m, Dhole.Pricing.Application.Services.ExwPickupPricePolicy.ResolveFixedCostAmount(
            110m, 60m, false, true, null));
    }
}

[TestClass]
public sealed class OwnLclMatrixFixedChargePolicyTests
{
    [TestMethod]
    public void ChinaConsolidationManejos_WithCatalogCostId_SurvivesSync()
    {
        var manejorCostId = Guid.Parse("e5b98707-6f1f-4772-b178-e440be0887fa");
        var notes = "LCL PROPIO · ConsolidadoId: e7baad63-d3a0-410f-a234-8b8e205aa0a3 · Consolidado: #54 · Base: HBL; cantidad aplicada: 1.";
        Assert.IsFalse(Dhole.Pricing.Application.Services.OwnLclMatrixFixedChargePolicy.ShouldRemove(
            manejorCostId, CostType.Fixed, notes));
    }

    [TestMethod]
    public void NormalAutoFixedCost_WithoutMatrixSource_CanBeRemoved()
    {
        Assert.IsTrue(Dhole.Pricing.Application.Services.OwnLclMatrixFixedChargePolicy.ShouldRemove(
            Guid.NewGuid(), CostType.Fixed, "Costo fijo automático de catálogo"));
    }

    [TestMethod]
    public void VariablePickupAndUnlinkedMatrixLine_AreRetained()
    {
        Assert.IsFalse(Dhole.Pricing.Application.Services.OwnLclMatrixFixedChargePolicy.ShouldRemove(
            Guid.NewGuid(), CostType.Variable, null));
        Assert.IsFalse(Dhole.Pricing.Application.Services.OwnLclMatrixFixedChargePolicy.ShouldRemove(
            null, CostType.Fixed, "LCL PROPIO · Consolidado: #54"));
    }
}

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
        Assert.AreEqual((Guid?)firstCostId, result[0].CostId);
        Assert.AreEqual((Guid?)detailId, result[1].Id);
        Assert.AreEqual((Guid?)savedCostId, result[1].CostId);
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
        Assert.AreEqual((Guid?)detailId, result[0].Id);
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
        Assert.AreEqual((Guid?)detailId, result[0].Id);
        Assert.IsNull(result[1].Id);
        Assert.AreEqual(125m, result[1].SaleAmount);
    }

    [TestMethod]
    public void CifEdit_ExcludesHistoricalFreightButPreservesEveryDestinationCharge()
    {
        var oldFreightId = Guid.NewGuid();
        var advanceManifestId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var freight = Hbl(oldFreightId, null) with
        {
            Name = "Flete Internacional",
            CostDetailType = CostDetailType.Freight,
        };
        var advanceManifest = Hbl(advanceManifestId, Guid.NewGuid()) with
        {
            Name = "Advance Manifest",
            CostAmount = 30m,
            SaleAmount = 35m,
        };
        var documentation = Hbl(docId, Guid.NewGuid()) with
        {
            Name = "Documentacion",
            CostAmount = 130m,
            SaleAmount = 140m,
        };

        var result = RateDetailUpdateInputNormalizer.WithoutQuotedFreight(
            [freight, advanceManifest, documentation],
            new HashSet<Guid> { oldFreightId });

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual((Guid?)advanceManifestId, result[0].Id);
        Assert.AreEqual("Advance Manifest", result[0].Name);
        Assert.AreEqual(35m, result[0].SaleAmount);
        Assert.AreEqual((Guid?)docId, result[1].Id);
        Assert.AreEqual(140m, result[1].SaleAmount);
    }

    [TestMethod]
    public void CifEdit_WhenAnotherChargeReusesOldFreightId_InsertsItInsteadOfUpdatingRemovedFreight()
    {
        var oldFreightId = Guid.NewGuid();
        var legitimateCostId = Guid.NewGuid();
        var destinationCharge = Hbl(oldFreightId, legitimateCostId) with
        {
            Name = "Release",
            SaleAmount = 95m,
        };

        var result = RateDetailUpdateInputNormalizer.WithoutQuotedFreight(
            [destinationCharge],
            new HashSet<Guid> { oldFreightId });

        Assert.AreEqual(1, result.Count);
        Assert.IsNull(result[0].Id);
        Assert.AreEqual((Guid?)legitimateCostId, result[0].CostId);
        Assert.AreEqual(95m, result[0].SaleAmount);
    }

    [TestMethod]
    public void CifEdit_WithoutHistoricalFreight_LeavesOtherDetailIdsUntouched()
    {
        var destinationDetailId = Guid.NewGuid();
        var destination = Hbl(destinationDetailId, Guid.NewGuid());

        var result = RateDetailUpdateInputNormalizer.WithoutQuotedFreight(
            [destination],
            new HashSet<Guid>());

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual((Guid?)destinationDetailId, result[0].Id);
    }

    [TestMethod]
    public void OwnLclIncompleteSnapshot_ProtectsInsuranceAndHandlingUnlessExplicitlyRemoved()
    {
        var freightId = Guid.NewGuid();
        var insuranceId = Guid.NewGuid();
        var handlingId = Guid.NewGuid();
        var rows = new (Guid Id, string Name, CostDetailType Type, string? Notes)[]
        {
            (freightId, "Flete internacional", CostDetailType.Freight, "LCL PROPIO · Consolidado: #55"),
            (insuranceId, "Seguro de carga", CostDetailType.Insurance, ""),
            (handlingId, "Manejos", CostDetailType.AgentCharge, ""),
        };
        var submitted = new[] { Hbl(freightId, null) with { Name = "Flete internacional", CostDetailType = CostDetailType.Freight } };

        var missing = RateDetailUpdateInputNormalizer.MissingOwnLclProtectedDetailIds(
            submitted, rows, new HashSet<Guid>());
        CollectionAssert.AreEquivalent(new[] { insuranceId, handlingId }, missing.ToArray());

        var explicitInsuranceRemoval = RateDetailUpdateInputNormalizer.MissingOwnLclProtectedDetailIds(
            submitted, rows, new HashSet<Guid> { insuranceId });
        CollectionAssert.AreEquivalent(new[] { handlingId }, explicitInsuranceRemoval.ToArray());
    }

    [TestMethod]
    public void OwnLclLegacyDestinationChargeNamedSeguroIsProtected()
    {
        var freightId = Guid.NewGuid();
        var oldInsuranceId = Guid.NewGuid();
        var persisted = new (Guid Id, string Name, CostDetailType Type, string? Notes)[]
        {
            (freightId, "Flete Internacional", CostDetailType.Freight, "LCL PROPIO · Consolidado: #55"),
            (oldInsuranceId, "Seguro de carga", CostDetailType.DestinationCharge, ""),
        };
        var submitted = new[] { Hbl(freightId, null) with { Name = "Flete Internacional", CostDetailType = CostDetailType.Freight } };
        var missing = RateDetailUpdateInputNormalizer.MissingOwnLclProtectedDetailIds(
            submitted, persisted, new HashSet<Guid>());
        CollectionAssert.AreEqual(new[] { oldInsuranceId }, missing.ToArray());
    }

    [TestMethod]
    public void OrdinaryNonOwnLclDoesNotBlockItsExistingUpdateSemantics()
    {
        var id = Guid.NewGuid();
        var rows = new (Guid Id, string Name, CostDetailType Type, string? Notes)[]
        {
            (id, "Seguro de carga", CostDetailType.Insurance, "LCL COLOADER"),
        };
        var missing = RateDetailUpdateInputNormalizer.MissingOwnLclProtectedDetailIds(
            Array.Empty<UpsertRateExtraDetailCommandItem>(), rows, new HashSet<Guid>());
        Assert.AreEqual(0, missing.Count);
    }

    [TestMethod]
    public void CompleteEditSnapshot_RemovesOnlyOmittedPersistedDetailIds()
    {
        var retainedVgmId = Guid.NewGuid();
        var staleVgmId = Guid.NewGuid();
        var retainedHblId = Guid.NewGuid();
        var newHandling = Hbl(Guid.NewGuid(), Guid.NewGuid()) with { Id = null };
        var snapshot = new[]
        {
            Hbl(retainedVgmId, Guid.NewGuid()) with { Name = "VGM", SaleAmount = 30m },
            Hbl(retainedHblId, Guid.NewGuid()),
            newHandling
        };

        var missing = RateDetailUpdateInputNormalizer.MissingPersistedDetailIds(
            snapshot, [retainedVgmId, staleVgmId, retainedHblId]);

        CollectionAssert.AreEquivalent(new[] { staleVgmId }, missing.ToArray());
    }

    [TestMethod]
    public void CompleteEditSnapshot_EmptyPayloadRemovesAllPriorDetails()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var missing = RateDetailUpdateInputNormalizer.MissingPersistedDetailIds(
            Array.Empty<UpsertRateExtraDetailCommandItem>(), [first, second]);
        CollectionAssert.AreEquivalent(new[] { first, second }, missing.ToArray());
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

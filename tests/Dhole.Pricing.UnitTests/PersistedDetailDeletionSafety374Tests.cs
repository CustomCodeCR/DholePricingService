using Dhole.Pricing.Application.Features.Rates.UpdateRate;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class PersistedDetailDeletionSafety374Tests
{
    private static UpsertRateExtraDetailCommandItem Detail(Guid id, string name) =>
        new(id, Guid.NewGuid(), name, CostDetailType.DestinationCharge,
            CostType.Fixed, Guid.NewGuid(), "USD", "USD", 100m, 120m,
            null, 1m, ChargeBasis.PerShipment);

    [TestMethod]
    public void OmittedPersistedDetails_AreDetectedById_EvenWhenNamesAreDuplicated()
    {
        var kept = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var submitted = new[] { Detail(kept, "HANDLING") };
        var ids = RateDetailUpdateInputNormalizer.MissingPersistedDetailIds(
            submitted, new[] { kept, missing });
        CollectionAssert.AreEquivalent(new[] { missing }, ids.ToArray());
    }

    [TestMethod]
    public void IdenticalNameDifferentId_DoesNotMaskMissingSavedRow()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var submitted = new[] { Detail(idA, "BL") };
        var ids = RateDetailUpdateInputNormalizer.MissingPersistedDetailIds(
            submitted, new[] { idA, idB });
        CollectionAssert.AreEqual(new[] { idB }, ids.ToArray());
    }
}

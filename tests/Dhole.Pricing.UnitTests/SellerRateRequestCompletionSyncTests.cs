using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class SellerRateRequestCompletionSyncTests
{
    private static RateRequest CreateModification()
    {
        var request = RateRequest.Create(
            RateRequestPriority.Red, Guid.NewGuid(), "Vendedor", "ventas@example.com",
            "Cliente", "Ejecutivo", "Lcl", "Shanghai", "Balboa",
            "{\"kind\":\"Modification\",\"modification\":{\"sourceRevisionNumber\":1}}");
        request.AttachRate(Guid.NewGuid());
        return request;
    }

    [TestMethod]
    public void SavedModification_CompletesWorkAndLeavesDeliveryPending()
    {
        var request = CreateModification();
        request.SetWorkStatus("Working", DateTime.UtcNow);
        request.PrepareModification(2);
        request.MarkSellerModificationReadyForDelivery(DateTime.UtcNow);

        Assert.AreEqual(RateRequestStatus.Completed, request.Status);
        Assert.AreEqual(2, request.PreparedRevisionNumber);
        Assert.IsNotNull(request.CompletedAtUtc);
        Assert.IsNull(request.DeliveryConfirmedAtUtc);
        Assert.IsNull(request.WorkStatus);
    }

    [TestMethod]
    public void SavedModification_RequiresARealPreparedRevision()
    {
        var request = CreateModification();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            request.MarkSellerModificationReadyForDelivery(DateTime.UtcNow));
        Assert.AreEqual(RateRequestStatus.Open, request.Status);
    }
}

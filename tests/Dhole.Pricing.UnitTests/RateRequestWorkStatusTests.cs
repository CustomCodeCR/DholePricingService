using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateRequestWorkStatusTests
{
    [TestMethod]
    public void WorkStatus_ChangesWithoutChangingRequestLifecycleOrDeadline()
    {
        var request = CreateRequest();
        var deadline = request.DueAtUtc;
        var updatedAt = DateTime.UtcNow;

        request.SetWorkStatus("Working", updatedAt);
        Assert.AreEqual("Working", request.WorkStatus);
        Assert.AreEqual(updatedAt, request.WorkStatusUpdatedAtUtc);
        Assert.AreEqual(RateRequestStatus.Open, request.Status);
        Assert.AreEqual(deadline, request.DueAtUtc);

        var requestedAt = updatedAt.AddMinutes(1);
        request.SetWorkStatus("RequestedFromProvider", requestedAt);
        Assert.AreEqual("RequestedFromProvider", request.WorkStatus);
        Assert.AreEqual(requestedAt, request.WorkStatusUpdatedAtUtc);
        Assert.AreEqual(RateRequestStatus.Open, request.Status);
        Assert.AreEqual(deadline, request.DueAtUtc);
    }

    [TestMethod]
    public void CompletingRequest_ClearsWorkStatus()
    {
        var request = CreateRequest();
        request.SetWorkStatus("Working", DateTime.UtcNow);

        request.MarkCompleted(DateTime.UtcNow);

        Assert.AreEqual(RateRequestStatus.Completed, request.Status);
        Assert.IsNull(request.WorkStatus);
        Assert.IsNull(request.WorkStatusUpdatedAtUtc);
    }

    [TestMethod]
    public void CancellingRequest_ClearsWorkStatus()
    {
        var request = CreateRequest();
        request.SetWorkStatus("RequestedFromProvider", DateTime.UtcNow);

        request.MarkCancelled("SellerCancelled", null, Guid.NewGuid(), "Ventas", DateTime.UtcNow);

        Assert.AreEqual(RateRequestStatus.Cancelled, request.Status);
        Assert.IsNull(request.WorkStatus);
        Assert.IsNull(request.WorkStatusUpdatedAtUtc);
    }

    [TestMethod]
    public void WorkStatus_CannotBeChangedAfterCompletion()
    {
        var request = CreateRequest();
        request.MarkCompleted(DateTime.UtcNow);
        Assert.ThrowsException<InvalidOperationException>(
            () => request.SetWorkStatus("Working", DateTime.UtcNow));
    }

    [TestMethod]
    public void WorkStatus_RejectsUnexpectedValues()
    {
        var request = CreateRequest();
        Assert.ThrowsException<ArgumentException>(
            () => request.SetWorkStatus("Completed", DateTime.UtcNow));
    }

    private static RateRequest CreateRequest() => RateRequest.Create(
        RateRequestPriority.Green,
        sellerUserId: Guid.NewGuid(),
        sellerName: "Ventas",
        sellerEmail: "vendedor@example.com",
        clientName: "Cliente de prueba",
        executiveName: null,
        shipmentMode: "LCL",
        originName: "Miami",
        destinationName: "Caldera",
        payloadJson: "{}");
}

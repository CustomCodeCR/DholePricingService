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
    public void ModificationIsNotPreparedJustByOpeningOrWorking()
    {
        var request = CreateRequest();
        request.AttachRate(Guid.NewGuid());
        request.SetWorkStatus("Working", DateTime.UtcNow);
        Assert.IsNull(request.PreparedRevisionNumber);

        request.PrepareModification(2);
        Assert.AreEqual(2, request.PreparedRevisionNumber);
        Assert.AreEqual(RateRequestStatus.Open, request.Status);
    }

    [TestMethod]
    public void SellerModificationCannotBeMarkedCompletedWithoutDelivery()
    {
        var request = CreateModification();
        request.SetWorkStatus("Working", DateTime.UtcNow);

        Assert.ThrowsException<InvalidOperationException>(() => request.MarkCompleted(DateTime.UtcNow));
        Assert.AreEqual(RateRequestStatus.Open, request.Status);
        Assert.IsNull(request.CompletedAtUtc);
        Assert.IsNull(request.DeliveryConfirmedAtUtc);
    }

    [TestMethod]
    public void SellerModificationCanOnlyCompleteWithPreparedDelivery()
    {
        var request = CreateModification();
        Assert.ThrowsException<InvalidOperationException>(
            () => request.MarkSellerModificationDelivered(DateTime.UtcNow));
        request.PrepareModification(2);
        Assert.AreEqual(RateRequestStatus.Open, request.Status);
        Assert.IsNull(request.DeliveryConfirmedAtUtc);

        request.MarkSellerModificationDelivered(DateTime.UtcNow);
        Assert.AreEqual(RateRequestStatus.Completed, request.Status);
        Assert.AreEqual(request.CompletedAtUtc, request.DeliveryConfirmedAtUtc);
    }

    [TestMethod]
    public void CompletedSellerRequest_CanAwaitMarginApprovalAndQueueDeliveryLater()
    {
        var request = CreateRequest();
        var rateId = Guid.NewGuid();
        var finishedAt = DateTime.UtcNow;
        request.AttachRate(rateId);
        request.MarkCompleted(finishedAt);

        Assert.AreEqual(RateRequestStatus.Completed, request.Status);
        Assert.AreEqual(rateId, request.RateId);
        Assert.IsNull(request.DeliveryConfirmedAtUtc);

        var queuedAt = finishedAt.AddMinutes(10);
        request.MarkDeliveryQueued(queuedAt);
        request.MarkDeliveryQueued(queuedAt.AddMinutes(1));
        Assert.AreEqual(queuedAt, request.DeliveryConfirmedAtUtc);
    }

    [TestMethod]
    public void SellerModification_CompletesAfterRevisionPreparedWithoutBypassingDelivery()
    {
        var request = CreateModification();
        request.AttachRate(Guid.NewGuid());

        Assert.ThrowsException<InvalidOperationException>(
            () => request.MarkSellerModificationReadyForDelivery(DateTime.UtcNow));
        request.PrepareModification(2);
        request.MarkSellerModificationReadyForDelivery(DateTime.UtcNow);

        Assert.AreEqual(RateRequestStatus.Completed, request.Status);
        Assert.IsNull(request.DeliveryConfirmedAtUtc);
        request.MarkDeliveryQueued(DateTime.UtcNow);
        Assert.IsNotNull(request.DeliveryConfirmedAtUtc);
    }

    [TestMethod]
    public void OpenOrUnlinkedRequest_CannotConfirmDelivery()
    {
        var request = CreateRequest();
        Assert.ThrowsException<InvalidOperationException>(
            () => request.MarkDeliveryQueued(DateTime.UtcNow));
        request.AttachRate(Guid.NewGuid());
        Assert.ThrowsException<InvalidOperationException>(
            () => request.MarkDeliveryQueued(DateTime.UtcNow));
    }

    [TestMethod]
    public void CompletedRequestCannotBePrepared()
    {
        var request = CreateRequest();
        request.MarkCompleted(DateTime.UtcNow);
        Assert.ThrowsException<InvalidOperationException>(
            () => request.PrepareModification(2));
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

    private static RateRequest CreateModification() => RateRequest.Create(
        RateRequestPriority.Red,
        sellerUserId: Guid.NewGuid(),
        sellerName: "Ventas",
        sellerEmail: "vendedor@example.com",
        clientName: "BRAME",
        executiveName: "Ventas",
        shipmentMode: "LTL",
        originName: "Colón",
        destinationName: "San Salvador",
        payloadJson: "{ \"kind\": \"Modification\", \"modification\": { \"sourceRevisionNumber\": 1 } }");

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

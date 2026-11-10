using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class SellerRateRequestEditingTests
{
    private static RateRequest NewRequest() => RateRequest.Create(
        RateRequestPriority.Green,
        sellerUserId: Guid.NewGuid(),
        sellerName: "Vendedor",
        sellerEmail: "seller@example.com",
        clientName: "Cliente original",
        executiveName: "Vendedor",
        shipmentMode: "Ftl",
        originName: "Alajuela",
        destinationName: "Managua",
        payloadJson: "{\"form\":{\"clientName\":\"Cliente original\"}}");

    [TestMethod]
    public void SyncCrmClientIdentity_UpdatesModificationWithoutOverwritingSellerChanges()
    {
        const string payload = "{\"kind\":\"Modification\",\"form\":{\"clientName\":\"Cliente anterior\"},\"modification\":{\"changes\":[{\"field\":\"cargo\",\"requested\":\"nuevo\"}]}}";
        var request = RateRequest.Create(
            RateRequestPriority.Red, Guid.NewGuid(), "Vendedor", "sales@example.com",
            "Cliente anterior", "Ejecutivo anterior", "Fcl", "Shanghái", "Caldera", payload);
        var rateId = Guid.NewGuid();
        request.AttachRate(rateId);
        request.SetWorkStatus("Working", DateTime.UtcNow);
        var due = request.DueAtUtc;

        Assert.IsTrue(request.IsSellerModificationRequest);
        Assert.IsTrue(request.SyncCrmClientIdentity(" Cliente CRM ", " Ejecutivo CRM "));
        Assert.AreEqual("Cliente CRM", request.ClientName);
        Assert.AreEqual("Ejecutivo CRM", request.ExecutiveName);
        Assert.AreEqual(payload, request.PayloadJson);
        Assert.AreEqual(rateId, request.RateId);
        Assert.AreEqual(due, request.DueAtUtc);
        Assert.AreEqual("Working", request.WorkStatus);
        Assert.AreEqual(RateRequestStatus.Open, request.Status);
        Assert.IsFalse(request.SyncCrmClientIdentity("Cliente CRM", "Ejecutivo CRM"));
    }

    [TestMethod]
    public void SyncCrmClientIdentity_KeepsOriginalExecutiveWhenCrmValueMissing()
    {
        var request = NewRequest();
        request.MarkCompleted(DateTime.UtcNow);
        Assert.IsTrue(request.SyncCrmClientIdentity("Cuenta vinculada", null));
        Assert.AreEqual("Cuenta vinculada", request.ClientName);
        Assert.AreEqual("Vendedor", request.ExecutiveName);
        Assert.AreEqual(RateRequestStatus.Completed, request.Status);
    }

    [TestMethod]
    public void UpdateSubmission_PreservesOwnershipAndOriginalSlaClock()
    {
        var request = NewRequest();
        var createdAt = request.RequestedAtUtc;
        var owner = request.SellerUserId;
        request.MarkSlaReminderSent(DateTime.UtcNow);
        request.UpdateSubmission(
            RateRequestPriority.Red,
            " Cliente nuevo ",
            "Vendedor",
            "Ltl",
            "Heredia",
            "Managua",
            "{\"form\":{\"clientName\":\"Cliente nuevo\"},\"supportDocuments\":[{\"id\":\"test\"}]}");

        Assert.AreEqual(owner, request.SellerUserId);
        Assert.AreEqual(createdAt, request.RequestedAtUtc);
        Assert.AreEqual(createdAt.AddHours(24), request.DueAtUtc);
        Assert.IsNull(request.SlaReminderSentAtUtc);
        Assert.AreEqual("Cliente nuevo", request.ClientName);
        Assert.AreEqual("Ltl", request.ShipmentMode);
        StringAssert.Contains(request.PayloadJson, "supportDocuments");
    }

    [TestMethod]
    public void AirLclRequest_AcceptsMissingPodAndKeepsPoe()
    {
        // Air LCL uses POE as its destination; a final maritime POD is optional.
        var request = RateRequest.Create(
            RateRequestPriority.Green,
            sellerUserId: Guid.NewGuid(),
            sellerName: "Vendedor",
            sellerEmail: "seller@example.com",
            clientName: "Cliente",
            executiveName: "Vendedor",
            shipmentMode: "Lcl",
            originName: "Miami",
            destinationName: "San José",
            payloadJson: "{\"form\":{\"modality\":\"Air\",\"shipmentMode\":\"LCL\"}}");

        var poeId = Guid.NewGuid();
        request.SetRoute(poeId, "San José", podId: null, podName: null);

        Assert.AreEqual("Lcl", request.ShipmentMode);
        Assert.AreEqual(poeId, request.PoeId);
        Assert.AreEqual("San José", request.PoeName);
        Assert.IsNull(request.PodId);
        Assert.IsNull(request.PodName);

        request.UpdateSubmission(RateRequestPriority.Yellow, "Cliente", "Vendedor",
            "Lcl", "Miami", "San José", "{\"form\":{\"modality\":\"Air\"}}");
        request.SetRoute(poeId, "San José", podId: null, podName: null);
        Assert.AreEqual(poeId, request.PoeId);
        Assert.IsNull(request.PodId);
    }

    [TestMethod]
    public void UpdateSubmission_RejectsRequestsLinkedToPricing()
    {
        var request = NewRequest();
        request.AttachRate(Guid.NewGuid());
        Assert.ThrowsExactly<InvalidOperationException>(() => request.UpdateSubmission(
            RateRequestPriority.Yellow, "Cliente", "Vendedor", "Ftl", "Origen", "Destino", "{}"));
    }

    [TestMethod]
    public void UpdateSubmission_RejectsCompletedRequests()
    {
        var request = NewRequest();
        request.MarkCompleted(DateTime.UtcNow);
        Assert.ThrowsExactly<InvalidOperationException>(() => request.UpdateSubmission(
            RateRequestPriority.Yellow, "Cliente", "Vendedor", "Ftl", "Origen", "Destino", "{}"));
    }
}

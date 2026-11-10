using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestCurrentIdentityTests
{
    [TestMethod]
    public void RequestDashboard_UsesCurrentQuotationClientWithoutChangingModificationHistory()
    {
        const string originalPayload =
            "{\"kind\":\"Modification\",\"form\":{\"clientName\":\"Cliente anterior\"},\"modification\":{\"changes\":[{\"field\":\"clientName\",\"original\":\"Cliente anterior\",\"requested\":\"Cliente nuevo\"}]}}";
        var request = RateRequest.Create(
            RateRequestPriority.Red, Guid.NewGuid(), "Vendedor", "vendedor@example.com",
            "Cliente anterior", "Ejecutivo anterior", "Fcl", "Qingdao", "Moín", originalPayload);
        var rateId = Guid.NewGuid();
        request.AttachRate(rateId);
        request.SetWorkStatus("Working", DateTime.UtcNow);
        var due = request.DueAtUtc;

        var unmatched = RateRequest.Create(
            RateRequestPriority.Green, Guid.NewGuid(), "Vendedor", "vendedor@example.com",
            "Sin vínculo", "Vendedor", "Lcl", "Origen", "Destino", "{}");
        var rates = new Dictionary<Guid, (string? ClientName, string? ExecutiveName)>
        {
            [rateId] = ("Cliente nuevo", "Ejecutivo CRM")
        };

        Assert.AreEqual(1, RateRequestCurrentIdentity.ApplySnapshots([request, unmatched], rates));
        Assert.AreEqual("Cliente nuevo", request.ClientName);
        Assert.AreEqual("Ejecutivo CRM", request.ExecutiveName);
        Assert.AreEqual(originalPayload, request.PayloadJson);
        Assert.AreEqual("Working", request.WorkStatus);
        Assert.AreEqual(RateRequestStatus.Open, request.Status);
        Assert.AreEqual(due, request.DueAtUtc);
        Assert.AreEqual("Sin vínculo", unmatched.ClientName);
        Assert.AreEqual(0, RateRequestCurrentIdentity.ApplySnapshots([request, unmatched], rates));
    }
}

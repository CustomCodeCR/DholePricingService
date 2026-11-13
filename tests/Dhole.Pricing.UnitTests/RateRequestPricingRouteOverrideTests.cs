using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateRequestPricingRouteOverrideTests
{
    private static RateRequest MakeRequest() => RateRequest.Create(
        RateRequestPriority.Green,
        sellerUserId: Guid.NewGuid(),
        sellerName: "Ventas",
        sellerEmail: null,
        clientName: "Cliente de prueba",
        executiveName: "Ejecutivo",
        shipmentMode: "Lcl",
        originName: "Shanghai",
        destinationName: "Caldera",
        payloadJson: """
        {"form":{"originId":"seller-pol","destinationId":"seller-poe","podId":"seller-pod","incotermCode":"FCA","cargoWeightKg":37},"requestContext":{"originName":"Shanghai","poeName":"Caldera","podName":"San José"}}
        """);

    [TestMethod]
    public void PricingChangesOnlyEffectiveRouteAndPreservesSalesPayload()
    {
        var entity = MakeRequest();
        var pol = Guid.NewGuid();
        var poe = Guid.NewGuid();
        var pod = Guid.NewGuid();
        var oldDueAt = entity.DueAtUtc;
        entity.OverridePricingRoute(pol, "Ningbo", poe, "Moín", pod, "San José", "Pricing");

        Assert.AreEqual("Ningbo", entity.OriginName);
        Assert.AreEqual("Moín", entity.DestinationName);
        Assert.AreEqual(poe, entity.PoeId);
        Assert.AreEqual(pod, entity.PodId);
        Assert.AreEqual(oldDueAt, entity.DueAtUtc);
        using var document = JsonDocument.Parse(entity.PayloadJson);
        var root = document.RootElement;
        Assert.AreEqual("seller-pol", root.GetProperty("form").GetProperty("originId").GetString());
        Assert.AreEqual("seller-poe", root.GetProperty("form").GetProperty("destinationId").GetString());
        Assert.AreEqual("seller-pod", root.GetProperty("form").GetProperty("podId").GetString());
        Assert.AreEqual("FCA", root.GetProperty("form").GetProperty("incotermCode").GetString());
        Assert.AreEqual(37, root.GetProperty("form").GetProperty("cargoWeightKg").GetInt32());
        Assert.AreEqual(pol.ToString(), root.GetProperty("pricingRouteOverride").GetProperty("polId").GetString());
        Assert.AreEqual(poe.ToString(), root.GetProperty("pricingRouteOverride").GetProperty("poeId").GetString());
        Assert.AreEqual("Moín", root.GetProperty("pricingRouteOverride").GetProperty("poeName").GetString());
    }

    [TestMethod]
    public void SubsequentOverridesKeepPriorPricingRouteInHistory()
    {
        var entity = MakeRequest();
        entity.OverridePricingRoute(Guid.NewGuid(), "Ningbo", Guid.NewGuid(), "Moín", null, null, "Pricing");
        entity.OverridePricingRoute(Guid.NewGuid(), "Qingdao", Guid.NewGuid(), "Caldera", null, null, "Pricing");
        using var document = JsonDocument.Parse(entity.PayloadJson);
        Assert.AreEqual("Ningbo", document.RootElement.GetProperty("pricingRouteHistory")[0].GetProperty("polName").GetString());
        Assert.AreEqual("Qingdao", document.RootElement.GetProperty("pricingRouteOverride").GetProperty("polName").GetString());
    }

    [TestMethod]
    public void ClosedRequestRejectsRouteChanges()
    {
        var entity = MakeRequest();
        entity.MarkCompleted(DateTime.UtcNow);
        Assert.ThrowsException<InvalidOperationException>(() =>
            entity.OverridePricingRoute(Guid.NewGuid(), "Ningbo", Guid.NewGuid(), "Moín", null, null, "Pricing"));
    }
}

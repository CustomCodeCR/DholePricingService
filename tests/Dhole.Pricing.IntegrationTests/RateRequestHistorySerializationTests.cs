using System.Reflection;
using System.Text.Json;
using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestHistorySerializationTests
{
    private static JsonElement SerializeRequest(string payload)
    {
        var request = RateRequest.Create(
            RateRequestPriority.Green,
            sellerUserId: Guid.NewGuid(),
            sellerName: "Vendedor",
            sellerEmail: "sales@example.com",
            clientName: "Cliente actual",
            executiveName: "Ejecutivo",
            shipmentMode: "Lcl",
            originName: "Shanghai",
            destinationName: "San José",
            payloadJson: payload);
        request.AttachRate(Guid.NewGuid());

        var method = typeof(RateRequestEndpoints).GetMethod(
            "ToDto", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "The history endpoint must preserve the request DTO contract.");
        var dto = method.Invoke(null, [request]);
        Assert.IsNotNull(dto);
        return JsonSerializer.SerializeToElement(dto);
    }

    [TestMethod]
    public void OriginalRequest_KeepsHistoricalSalesForm()
    {
        var dto = SerializeRequest("{\"form\":{\"clientName\":\"Cliente original\",\"shipmentMode\":\"LCL\"}}");
        Assert.AreEqual("New", dto.GetProperty("kind").GetString());
        Assert.AreEqual("Cliente original", dto.GetProperty("payload")
            .GetProperty("form").GetProperty("clientName").GetString());
    }

    [TestMethod]
    public void ModificationRequest_KeepsSourceRevisionAndRequestedChanges()
    {
        var dto = SerializeRequest(
            "{\"kind\":\"Modification\",\"form\":{\"clientName\":\"Cliente solicitado\"}," +
            "\"modification\":{\"sourceRevisionNumber\":3,\"notes\":\"Cambio de ruta\"," +
            "\"changes\":[{\"field\":\"POE\",\"action\":\"Modificar\"," +
            "\"original\":\"Balboa\",\"requested\":\"Caldera\"}]}}");

        Assert.AreEqual("Modification", dto.GetProperty("kind").GetString());
        var payload = dto.GetProperty("payload");
        Assert.AreEqual("Cliente solicitado", payload.GetProperty("form")
            .GetProperty("clientName").GetString());
        Assert.AreEqual(3, payload.GetProperty("modification")
            .GetProperty("sourceRevisionNumber").GetInt32());
        Assert.AreEqual("Caldera", payload.GetProperty("modification")
            .GetProperty("changes")[0].GetProperty("requested").GetString());
    }
    [TestMethod]
    public void ReturnedRequest_ReusesIdentityAndPreservesEveryReviewInAuditHistory()
    {
        var sellerId = Guid.NewGuid();
        var pricingActorId = Guid.NewGuid();
        var request = RateRequest.Create(
            RateRequestPriority.Green,
            sellerUserId: sellerId,
            sellerName: "Ventas",
            sellerEmail: "ventas@example.com",
            clientName: "Cliente",
            executiveName: "Ejecutivo",
            shipmentMode: "Lcl",
            originName: "Shanghai",
            destinationName: "San José",
            payloadJson: "{\"form\":{\"cargo\":\"inicial\"}}");
        var originalId = request.Id;

        request.ReturnToSeller("Falta detallar el tipo de carga.", pricingActorId, "Pricing Uno", DateTime.UtcNow);
        Assert.AreEqual(RateRequestStatus.Returned, request.Status);

        request.UpdateSubmission(
            RateRequestPriority.Green, "Cliente", "Ejecutivo", "Lcl",
            "Shanghai", "San José", "{\"form\":{\"cargo\":\"corregida\"}}");
        Assert.AreEqual(originalId, request.Id, "A resubmission must never create a new request.");
        Assert.AreEqual(RateRequestStatus.Open, request.Status);

        request.ReturnToSeller("La documentación aún está incompleta.", pricingActorId, "Pricing Dos", DateTime.UtcNow);
        using var historyDocument = JsonDocument.Parse(request.PayloadJson);
        var root = historyDocument.RootElement;
        var history = root.GetProperty("pricingReturnHistory");
        Assert.AreEqual(2, history.GetArrayLength());
        Assert.AreEqual("Falta detallar el tipo de carga.", history[0].GetProperty("reason").GetString());
        Assert.AreEqual("La documentación aún está incompleta.", history[1].GetProperty("reason").GetString());
        Assert.AreEqual("Pricing Uno", history[0].GetProperty("returnedByName").GetString());
        Assert.AreEqual("Pricing Dos", history[1].GetProperty("returnedByName").GetString());
        Assert.AreEqual(pricingActorId.ToString(), history[0].GetProperty("returnedByUserId").GetString());
        Assert.IsTrue(history[1].TryGetProperty("returnedAtUtc", out _));
    }

}

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
}

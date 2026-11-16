using System;
using System.Text.Json;
using Dhole.Pricing.Api.Endpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class OwnLclLinkedQuoteVolumeTests
{
    [TestMethod]
    public void LinkedQuoteSerializesPhysicalAndBillableCbmSeparately()
    {
        var quote = new OwnLclLinkedQuoteDto(
            Guid.NewGuid(), "QUO-00000-000001", "Cliente", "Sent",
            DateTime.UtcNow, 0.118m, 1m);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            quote, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.AreEqual(0.118m, json.RootElement.GetProperty("physicalCbm").GetDecimal());
        Assert.AreEqual(1m, json.RootElement.GetProperty("billableCbm").GetDecimal());
        Assert.AreEqual("QUO-00000-000001", json.RootElement.GetProperty("quoNumber").GetString());
    }
}

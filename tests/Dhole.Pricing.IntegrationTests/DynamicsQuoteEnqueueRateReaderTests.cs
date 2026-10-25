using System.Text.Json;
using Dhole.Pricing.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class DynamicsQuoteEnqueueRateReaderTests
{
    private const string LegacyRow = """
        {
          "id":"d18d7264-f586-4652-94b3-b63e0e42ee3f",
          "revision_number":3,
          "is_deleted":false,
          "status":"AcceptedByClient",
          "idtra_number":"IDTRA999.2026-001",
          "rate_code":"QUO-001",
          "rate_name":"Tarifa comercial",
          "client_name":"Cliente",
          "total_cost_amount":1200.25,
          "total_sale_amount":1500.50,
          "currency_code":"USD",
          "shipment_mode":"Fcl",
          "rate_type":"Spot",
          "container_quantity":1,
          "container_type_name":"40 HC",
          "container_type_code":"40HC",
          "chargeable_quantity":1,
          "valid_from":"2026-10-20T00:00:00",
          "valid_to":"2026-11-20T00:00:00",
          "pol_name":"SHANGHAI",
          "poe_name":"CALDERA",
          "pod_name":null,
          "includes":"Seguro",
          "subject_to":null,
          "excludes":null,
          "transit_time":null,
          "unused_legacy_relation":null
        }
        """;

    [TestMethod]
    public void ReadsHistoricalSnakeCaseWithoutHydratingLegacyAggregate()
    {
        var rate = DynamicsQuoteEnqueueRateReader.Parse(LegacyRow);
        Assert.AreEqual(Guid.Parse("d18d7264-f586-4652-94b3-b63e0e42ee3f"), rate.Id);
        Assert.AreEqual(3, rate.RevisionNumber);
        Assert.AreEqual("AcceptedByClient", rate.Status);
        Assert.AreEqual("Spot", rate.RateType);
        Assert.AreEqual(1500.50m, rate.TotalSaleAmount);
        using var snapshot = JsonDocument.Parse(rate.ToPayloadJson(new[]
        {
            new { ContainerTypeCode = "40HC", ContainerTypeName = "40 HC", Quantity = 1 }
        }));
        var root = snapshot.RootElement;
        Assert.AreEqual("USD", root.GetProperty("currencyCode").GetString());
        Assert.AreEqual(1200.25m, root.GetProperty("totalCostAmount").GetDecimal());
        Assert.AreEqual("Fcl", root.GetProperty("shipmentMode").GetString());
        Assert.AreEqual(1, root.GetProperty("containers")[0].GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public void AcceptsPascalCaseDatabaseColumns()
    {
        var pascal = LegacyRow
            .Replace("\"revision_number\"", "\"RevisionNumber\"")
            .Replace("\"total_sale_amount\"", "\"TotalSaleAmount\"")
            .Replace("\"shipment_mode\"", "\"ShipmentMode\"");
        Assert.AreEqual(1500.50m, DynamicsQuoteEnqueueRateReader.Parse(pascal).TotalSaleAmount);
    }

    [TestMethod]
    public void RefusesMissingAmountInsteadOfInventingMoney()
    {
        var invalid = LegacyRow.Replace("\"total_sale_amount\":1500.50",
            "\"total_sale_amount\":null");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            DynamicsQuoteEnqueueRateReader.Parse(invalid));
    }

    [TestMethod]
    public void RefusesUnknownShipmentModeInsteadOfSendingWrongQuote()
    {
        var invalid = LegacyRow.Replace("\"shipment_mode\":\"Fcl\"",
            "\"shipment_mode\":\"UnknownMode\"");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            DynamicsQuoteEnqueueRateReader.Parse(invalid));
    }
}

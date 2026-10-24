using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Dhole.Pricing.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class DynamicsQuotePayloadBuilderTests
{
    private const string Schema = """
        {
          "name":"crear_oferta","version":1,"entityName":"quote",
          "operation":"create","connectionCode":"principal",
          "fields":[
            {"name":"name","target":"name","type":"string","dataverseType":"String","required":true,"options":[]},
            {"name":"effectivefrom","target":"effectivefrom","type":"datetime","dataverseType":"DateTime","required":false,"options":[]},
            {"name":"msdyn_invoicesetuptotals","target":"msdyn_invoicesetuptotals","type":"decimal","dataverseType":"Money","required":true,"options":[]},
            {"name":"new_equipo","target":"new_equipo","type":"integer","dataverseType":"Picklist","required":true,
             "options":[{"label":"LCL","value":100000000},{"label":"40 HC FCL","value":100000005}]},
            {"name":"new_flete","target":"new_flete","type":"decimal","dataverseType":"Money","required":true,"options":[]},
            {"name":"new_monto9","target":"new_monto9","type":"decimal","dataverseType":"Money","required":true,"options":[]},
            {"name":"new_categoriatarifa","target":"new_categoriatarifa","type":"integer","dataverseType":"Picklist","required":true,
             "options":[{"label":"Categoría","value":100000001}]},
            {"name":"new_preestado","target":"new_preestado","type":"integer","dataverseType":"Picklist","required":true,
             "options":[{"label":"Preestado","value":100000003}]},
            {"name":"new_proyecctoespecifico","target":"new_proyecctoespecifico","type":"integer","dataverseType":"Picklist","required":true,
             "options":[{"label":"Proyecto","value":100000004}]},
            {"name":"new_tipodecliente","target":"new_tipodecliente","type":"integer","dataverseType":"Picklist","required":true,
             "options":[{"label":"Cliente","value":100000000}]},
            {"name":"customeridtype","target":"customeridtype","type":"string","dataverseType":"EntityName","required":true,
             "options":[{"label":"Quote","value":"quote"}]},
            {"name":"new_idtraasignado","target":"new_idtraasignado","type":"string","dataverseType":"String","required":false,"options":[]},
            {"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}
          ]
        }
        """;

    private const string Accepted = """
        {
          "status":"AcceptedByClient","revisionNumber":2,
          "idtraNumber":"IDTRA-2026-045","rateCode":"QUO-2026-003",
          "rateName":"Oferta 40HC","clientName":"Cliente Prueba",
          "shipmentMode":"Fcl","containerTypeName":"40 HC",
          "containerTypeCode":"40HC","containerQuantity":2,
          "currencyCode":"USD","totalCostAmount":1800.75,
          "totalSaleAmount":2400.25,
          "validFrom":"2026-10-20T00:00:00Z",
          "validTo":"2026-11-20T00:00:00Z",
          "containers":[{"containerTypeName":"40 HC","containerTypeCode":"40HC","quantity":2}]
        }
        """;

    [TestMethod]
    public void MapsApprovedQuoteWithoutGuessingEquipmentOrPrice()
    {
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            DynamicsQuotePayloadBuilder.Build(Accepted, schema)));
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.AreEqual("principal", doc.RootElement.GetProperty("connectionCode").GetString());
        Assert.AreEqual(1800.75m, inputs.GetProperty("new_flete").GetDecimal());
        Assert.AreEqual(1800.75m, inputs.GetProperty("new_monto9").GetDecimal());
        Assert.AreEqual(2400.25m, inputs.GetProperty("msdyn_invoicesetuptotals").GetDecimal());
        Assert.AreEqual(100000005, inputs.GetProperty("new_equipo").GetInt32());
        Assert.AreEqual(100000001, inputs.GetProperty("new_categoriatarifa").GetInt32());
        Assert.AreEqual(100000003, inputs.GetProperty("new_preestado").GetInt32());
        Assert.AreEqual(100000004, inputs.GetProperty("new_proyecctoespecifico").GetInt32());
        Assert.AreEqual(100000000, inputs.GetProperty("new_tipodecliente").GetInt32());
        Assert.AreEqual("quote", inputs.GetProperty("customeridtype").GetString());
        Assert.AreEqual("IDTRA-2026-045", inputs.GetProperty("new_idtraasignado").GetString());
        Assert.AreEqual("2 x 40 HC", inputs.GetProperty("new_cantidadequipos").GetString());
    }

    [TestMethod]
    public void LclUsesItsOwnDynamicsOption()
    {
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        var lcl = Accepted.Replace("\"shipmentMode\":\"Fcl\"", "\"shipmentMode\":\"Lcl\"");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            DynamicsQuotePayloadBuilder.Build(lcl, schema)));
        Assert.AreEqual(100000000, doc.RootElement.GetProperty("inputs")
            .GetProperty("new_equipo").GetInt32());
    }

    [TestMethod]
    public void UnapprovedOrIdtraMissingNeverProducesAPayload()
    {
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.Build(
                Accepted.Replace("AcceptedByClient", "PendingApproval"), schema));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.Build(
                Accepted.Replace("IDTRA-2026-045", ""), schema));
    }

    [TestMethod]
    public void MissingInvoiceSetupTotalsDoesNotBlockStandardQuoteLineTotal()
    {
        // invoice setup totals is not the same as the standard quote.totalamount.
        var withoutInvoiceSetupField = Schema.Replace(
            """{"name":"msdyn_invoicesetuptotals","target":"msdyn_invoicesetuptotals","type":"decimal","dataverseType":"Money","required":true,"options":[]},""",
            "");
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(withoutInvoiceSetupField);
        using var mapped = JsonDocument.Parse(JsonSerializer.Serialize(
            DynamicsQuotePayloadBuilder.Build(Accepted, schema)));
        var inputs = mapped.RootElement.GetProperty("inputs");
        Assert.AreEqual(1800.75m, inputs.GetProperty("new_flete").GetDecimal());
        Assert.AreEqual(1800.75m, inputs.GetProperty("new_monto9").GetDecimal());
        Assert.IsFalse(inputs.TryGetProperty("msdyn_invoicesetuptotals", out _));
        Assert.AreEqual("IDTRA-2026-045",
            inputs.GetProperty("new_idtraasignado").GetString());
    }


    [TestMethod]
    public void ActiveQuoteWithoutOptionalIdtraTextAndCustomerTypeStillBuilds()
    {
        // Matches the 22-field "crear_oferta" setup: customer and IDTRA are
        // name-resolved relationships, while the optional text fields are absent.
        var oldFields = new[]
        {
            """{"name":"customeridtype","target":"customeridtype","type":"string","dataverseType":"EntityName","required":true,"options":[{"label":"Quote","value":"quote"}]},""",
            """{"name":"new_idtraasignado","target":"new_idtraasignado","type":"string","dataverseType":"String","required":false,"options":[]},"""
        };
        var schemaText = Schema;
        foreach (var oldField in oldFields)
            schemaText = schemaText.Replace(oldField, "", StringComparison.Ordinal);
        schemaText = schemaText.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            """{"name":"customerid","target":"customerid_account@odata.bind","type":"string","dataverseType":"Customer","required":true,"options":[]},
              {"name":"new_idtra1","target":"new_idtra1_incident@odata.bind","type":"string","dataverseType":"Lookup","required":true,"options":[]},
              {"name":"msdyn_account","target":"msdyn_account_account@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
              {"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            DynamicsQuotePayloadBuilder.Build(Accepted, schema)));
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.AreEqual("Cliente Prueba", inputs.GetProperty("customerid").GetString());
        Assert.AreEqual("Cliente Prueba", inputs.GetProperty("msdyn_account").GetString());
        Assert.AreEqual("IDTRA-2026-045", inputs.GetProperty("new_idtra1").GetString());
        Assert.IsFalse(inputs.TryGetProperty("customeridtype", out _));
        Assert.IsFalse(inputs.TryGetProperty("new_idtraasignado", out _));
    }

    [TestMethod]
    public void IdtraMustBeMappedToAnAuthorizedField()
    {
        var schemaText = Schema.Replace(
            """{"name":"new_idtraasignado","target":"new_idtraasignado","type":"string","dataverseType":"String","required":false,"options":[]},""",
            "", StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.Build(Accepted, schema));
    }

    [TestMethod]
    public void SingleMetadataChoiceIsUsedWhenOldDefaultIsNotAvailable()
    {
        var altered = Schema.Replace("100000001", "100000000", StringComparison.Ordinal)
            .Replace("100000003", "100000000", StringComparison.Ordinal)
            .Replace("100000004", "100000000", StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(altered);
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(
            DynamicsQuotePayloadBuilder.Build(Accepted, schema)));
        var inputs = parsed.RootElement.GetProperty("inputs");
        Assert.AreEqual(100000000, inputs.GetProperty("new_categoriatarifa").GetInt32());
        Assert.AreEqual(100000000, inputs.GetProperty("new_preestado").GetInt32());
        Assert.AreEqual(100000000, inputs.GetProperty("new_proyecctoespecifico").GetInt32());
    }

    [TestMethod]
    public void AmbiguousMetadataChoicesRequireExplicitOptionConfiguration()
    {
        var altered = Schema.Replace(
            """{"label":"Categoría","value":100000001}""",
            """{"label":"Categoría A","value":100000000},{"label":"Categoría B","value":100000002}""",
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(altered);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.Build(Accepted, schema));
        var options = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string,string?> {
                ["DynamicsQuoteSync:Choices:new_categoriatarifa"] = "100000002"
            }).Build();
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(
            DynamicsQuotePayloadBuilder.Build(Accepted, schema, options)));
        Assert.AreEqual(100000002, parsed.RootElement.GetProperty("inputs")
            .GetProperty("new_categoriatarifa").GetInt32());
    }

    [TestMethod]
    public void UnknownEquipmentFailsInsteadOfPickingAnArbitraryCode()
    {
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.Build(
                Accepted.Replace("40 HC", "20 Flat Rack")
                    .Replace("40HC", "20FLAT"), schema));
    }
    [TestMethod]
    public void ParsesFirstIdtraSegmentAndKeepsEmbeddedIdtraPrefixes()
    {
        CollectionAssert.AreEqual(
            new[] { "IDTRA999.12565" },
            DynamicsQuotePayloadBuilder.ParseIdtraNumbers(
                "IDTRA IDTRA999.12565 // QUO-00000-000316 // QUO-11229-Q4X8M7"));
        CollectionAssert.AreEqual(
            new[] { "IDTRA999.12565", "IDTRA999.12566" },
            DynamicsQuotePayloadBuilder.ParseIdtraNumbers(
                "IDTRA IDTRA999.12565, IDTRA999.12566 // QUO-00000-000316"));
        CollectionAssert.AreEqual(
            new[] { "IDTRA-2026-00125" },
            DynamicsQuotePayloadBuilder.ParseIdtraNumbers(
                "IDTRA-2026-00125 // QUO-00000-000316"));
    }

    [TestMethod]
    public void MultipleIdtrasFillTheirDistinctApprovedLookupSlots()
    {
        var schemaText = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            """{"name":"new_idtra1","target":"new_idtra1_incident@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
              {"name":"new_idtra2","target":"new_idtra2_incident@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
              {"name":"new_idtra3","target":"new_idtra3_incident@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
              {"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            StringComparison.Ordinal);
        var multiSnapshot = Accepted.Replace("IDTRA-2026-045",
            "IDTRA IDTRA999.12565, IDTRA999.12566 // QUO-00000-000316 // QUO-11229-Q4X8M7",
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            DynamicsQuotePayloadBuilder.Build(multiSnapshot, schema)));
        var inputs = result.RootElement.GetProperty("inputs");
        Assert.AreEqual("IDTRA999.12565", inputs.GetProperty("new_idtra1").GetString());
        Assert.AreEqual("IDTRA999.12566", inputs.GetProperty("new_idtra2").GetString());
        Assert.IsFalse(inputs.TryGetProperty("new_idtra3", out _));
        Assert.AreEqual("IDTRA999.12565, IDTRA999.12566",
            inputs.GetProperty("new_idtraasignado").GetString());
    }

    [TestMethod]
    public void MultipleIdtraNeverSilentlyDiscardsMissingLookupSlots()
    {
        var schemaText = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            """{"name":"new_idtra1","target":"new_idtra1_incident@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
              {"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.Build(
                Accepted.Replace("IDTRA-2026-045",
                    "IDTRA IDTRA999.12565, IDTRA999.12566 // QUO-00000-000316",
                    StringComparison.Ordinal), schema));
    }

    [TestMethod]
    public void MoreThanNineIdtraCannotBeSilentlyTruncated()
    {
        var idtras = string.Join(",", Enumerable.Range(1, 10).Select(x => "IDTRA" + x));
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.Build(
                Accepted.Replace("IDTRA-2026-045", idtras, StringComparison.Ordinal),
                schema));
    }

    [TestMethod]
    public void AdditionalAmountsAndCurrencyMapWithoutDuplicatingSupplierCost()
    {
        const string extraFields = """
            {"name":"new_monto1","target":"new_monto1","type":"decimal","dataverseType":"Money","required":false,"options":[]},
            {"name":"new_monto2","target":"new_monto2","type":"decimal","dataverseType":"Money","required":false,"options":[]},
            {"name":"new_monto9","target":"new_monto9","type":"decimal","dataverseType":"Money","required":false,"options":[]},
            {"name":"new_divisa","target":"new_divisa","type":"string","dataverseType":"String","required":false,"options":[]},
            """;
        var expanded = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            extraFields + "\n" +
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            StringComparison.Ordinal);
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            DynamicsQuotePayloadBuilder.Build(Accepted, DynamicsQuotePayloadBuilder.ReadSchema(expanded))));
        var inputs = result.RootElement.GetProperty("inputs");
        Assert.AreEqual(1800.75m, inputs.GetProperty("new_monto1").GetDecimal());
        Assert.AreEqual(0m, inputs.GetProperty("new_monto2").GetDecimal());
        Assert.AreEqual(0m, inputs.GetProperty("new_monto9").GetDecimal());
        Assert.AreEqual("USD", inputs.GetProperty("new_divisa").GetString());
    }

    [TestMethod]
    public void CalculatedSupplierCostCannotBeWrittenByPricingTemplate()
    {
        const string calculated = """
            {"name":"new_totalcostoproveedor","target":"new_totalcostoproveedor","type":"decimal","dataverseType":"Decimal","required":false,"options":[]},
            """;
        var schemaText = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            calculated + "\n" +
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.Build(Accepted, schema));
        StringAssert.Contains(error.Message, "calculated and read-only");
    }

    [TestMethod]
    public void TenthIdtraIsMappedIfActiveTemplateApprovesTenLookups()
    {
        const string original = """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""";
        var lookups = string.Join(",\n", Enumerable.Range(1, 10).Select(i =>
            "{\"name\":\"new_idtra" + i + "\",\"target\":\"new_idtra" + i +
            "_incident@odata.bind\",\"type\":\"string\",\"dataverseType\":\"Lookup\",\"required\":false,\"options\":[]}"));
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema.Replace(original,
            lookups + ",\n" + original, StringComparison.Ordinal));
        var csv = string.Join(", ", Enumerable.Range(1, 10).Select(i => "IDTRA" + i));
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            DynamicsQuotePayloadBuilder.Build(
                Accepted.Replace("IDTRA-2026-045", csv, StringComparison.Ordinal), schema)));
        var inputs = result.RootElement.GetProperty("inputs");
        Assert.AreEqual("IDTRA10", inputs.GetProperty("new_idtra10").GetString());
        Assert.AreEqual(csv, inputs.GetProperty("new_idtraasignado").GetString());
    }

}

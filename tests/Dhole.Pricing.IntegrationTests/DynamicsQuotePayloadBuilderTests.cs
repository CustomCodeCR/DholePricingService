using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Rates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class DynamicsQuotePayloadBuilderTests
{
    private static readonly Guid TestDholeRateId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static object BuildForTest(string acceptedSnapshot, DynamicsCrudSchema schema,
        IConfiguration? configuration = null, Guid? resolvedCurrencyId = null) =>
        DynamicsQuotePayloadBuilder.Build(acceptedSnapshot,schema,configuration,
            resolvedCurrencyId,TestDholeRateId);

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
            {"name":"new_notifrespuestacotizacion","target":"new_notifrespuestacotizacion","type":"integer","dataverseType":"Picklist","required":false,
             "options":[{"label":"Notif. Respuesta Cotizacion","value":100000000}]},
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
    public void QuoteUpsertIdentityRejectsMissingGuidAndNeverStoresIdtraCsv()
    {
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.Build(Accepted,schema));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted,schema)));
        var value = document.RootElement.GetProperty("inputs")
            .GetProperty("new_idtraasignado").GetString();
        Assert.AreEqual(TestDholeRateId.ToString("D"),value);
        Assert.IsFalse(value!.Contains("IDTRA",StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void UpsertTemplatePreservesAutomaticSaleLineInputsAndTrustedRateIdentity()
    {
        var upsertSchema = Schema.Replace(
            "\"operation\":\"create\"", "\"operation\":\"upsert\"",
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(upsertSchema);
        Assert.AreEqual("upsert", schema.Operation);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, schema)));
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.AreEqual(2400.25m, inputs.GetProperty("new_flete").GetDecimal());
        Assert.AreEqual(0m, inputs.GetProperty("new_monto9").GetDecimal());
        Assert.AreEqual(TestDholeRateId.ToString("D"),
            inputs.GetProperty("new_idtraasignado").GetString());
    }

    [TestMethod]
    public void MapsApprovedQuoteWithoutGuessingEquipmentOrPrice()
    {
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, schema)));
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.AreEqual("principal", doc.RootElement.GetProperty("connectionCode").GetString());
        Assert.AreEqual(2400.25m, inputs.GetProperty("new_flete").GetDecimal());
        Assert.AreEqual(0m, inputs.GetProperty("new_monto9").GetDecimal());
        Assert.AreEqual(2400.25m, inputs.GetProperty("msdyn_invoicesetuptotals").GetDecimal());
        Assert.AreEqual(100000005, inputs.GetProperty("new_equipo").GetInt32());
        Assert.AreEqual(100000001, inputs.GetProperty("new_categoriatarifa").GetInt32());
        Assert.AreEqual(100000003, inputs.GetProperty("new_preestado").GetInt32());
        Assert.AreEqual(100000004, inputs.GetProperty("new_proyecctoespecifico").GetInt32());
        Assert.AreEqual(100000000, inputs.GetProperty("new_tipodecliente").GetInt32());
        Assert.IsFalse(inputs.TryGetProperty("customeridtype", out _));
        Assert.AreEqual(TestDholeRateId.ToString("D"), inputs.GetProperty("new_idtraasignado").GetString());
        Assert.AreEqual("2 x 40 HC", inputs.GetProperty("new_cantidadequipos").GetString());
    }

    [TestMethod]
    public void UpsertUsesCr933RateIdAndNeverConfusesItWithIdtra()
    {
        var schemaText = Schema.Replace(
            "\"operation\":\"create\"", "\"operation\":\"upsert\"",
            StringComparison.Ordinal).Replace(
            """{"name":"new_idtraasignado","target":"new_idtraasignado","type":"string","dataverseType":"String","required":false,"options":[]}""",
            """{"name":"cr933_dholerateid","target":"cr933_dholerateid","type":"string","dataverseType":"String","required":true,"options":[]}""",
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, schema)));
        var inputs = document.RootElement.GetProperty("inputs");
        Assert.AreEqual(TestDholeRateId.ToString("D"),
            inputs.GetProperty("cr933_dholerateid").GetString());
        Assert.IsFalse(inputs.TryGetProperty("new_idtraasignado", out _));
        Assert.IsFalse(inputs.GetProperty("cr933_dholerateid").GetString()!
            .Contains("IDTRA", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(2400.25m, inputs.GetProperty("new_flete").GetDecimal());
    }

    [TestMethod]
    public void MapsAllNineCostAmountsAndTotalSaleIndependently()
    {
        const string oldCostField =
            """{"name":"new_monto9","target":"new_monto9","type":"decimal","dataverseType":"Money","required":true,"options":[]}""";
        var allCostFields = string.Join(",", Enumerable.Range(1, 9).Select(i =>
            "{\"name\":\"new_monto" + i + "\",\"target\":\"new_monto" + i +
            "\",\"type\":\"decimal\",\"dataverseType\":\"Money\",\"required\":true,\"options\":[]}"));
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(
            Schema.Replace(oldCostField, allCostFields, StringComparison.Ordinal));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, schema)));
        var inputs = document.RootElement.GetProperty("inputs");
        Assert.AreEqual(2400.25m, inputs.GetProperty("new_flete").GetDecimal());
        Assert.AreEqual(1800.75m, inputs.GetProperty("new_monto1").GetDecimal());
        for (var index = 2; index <= 9; index++)
            Assert.AreEqual(0m, inputs.GetProperty("new_monto" + index).GetDecimal());
    }

    [TestMethod]
    public void LclUsesItsOwnDynamicsOption()
    {
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        var lcl = Accepted.Replace("\"shipmentMode\":\"Fcl\"", "\"shipmentMode\":\"Lcl\"");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(lcl, schema)));
        Assert.AreEqual(100000000, doc.RootElement.GetProperty("inputs")
            .GetProperty("new_equipo").GetInt32());
    }


    private static DynamicsCrudSchema WithEquipmentOptions(params string[] labels)
    {
        var root = JsonNode.Parse(Schema)!.AsObject();
        var equipment = root["fields"]!.AsArray()
            .Select(field => field!.AsObject())
            .Single(field => field["name"]!.ToString() == "new_equipo");
        var options = new JsonArray();
        for (var i = 0; i < labels.Length; i++)
            options.Add(new JsonObject
            {
                ["label"] = labels[i],
                ["value"] = 100000010 + i
            });
        equipment["options"] = options;
        return DynamicsQuotePayloadBuilder.ReadSchema(root.ToJsonString());
    }

    private static string WithEquipment(string name, string mode)
    {
        var rate = JsonNode.Parse(Accepted)!.AsObject();
        rate["shipmentMode"] = mode;
        rate["containerTypeName"] = name;
        rate["containerTypeCode"] = name;
        var container = rate["containers"]!.AsArray()[0]!.AsObject();
        container["containerTypeName"] = name;
        container["containerTypeCode"] = name;
        return rate.ToJsonString();
    }

    [TestMethod]
    public void EveryKnownDynamicsEquipmentCatalogLabelResolvesItsExactPhysicalType()
    {
        // Complete catalog supplied by the active Dataverse new_equipo choice.
        var equipment = new (string Source, string Mode, string Choice)[]
        {
            ("20 STD", "Fcl", "20 STD FCL"),
            ("20 OT", "Fcl", "20' OT"),
            ("20 STD - 40 STD - 40 HC", "Fcl", "20STD - 40STD - 40HC FCL"),
            ("24 TON", "Ftl", "24 TON FTL"),
            ("26", "Ftl", "26\" FTL"),
            ("40 Flat Rack", "Fcl", "40 Flat Racks."),
            ("40 HC", "Fcl", "40 HC FCL"),
            ("40 NOR", "Fcl", "40 NOR FCL"),
            ("40 Open Top", "Fcl", "40 Open Top"),
            ("40 Reefer", "Fcl", "40 REFF"),
            ("40 STD", "Fcl", "40 STD FCL"),
            ("40 STD - 40 HC", "Fcl", "40 STD FCL - 40 HC FCL"),
            ("45 HC", "Fcl", "45 HC FCL"),
            ("48", "Ftl", "48\" FTL"),
            ("53", "Ftl", "53\" FTL"),
            ("24 pies / 7 TON", "Ftl", "7 Ton / 24 pies"),
            ("8 TON", "Ftl", "8 TON FTL"),
            ("CARRETA", "Ftl", "CARRETA"),
            ("LCL", "Lcl", "LCL"),
            ("LOW BOY", "Ftl", "LOW BOY"),
            ("LTL", "Ltl", "LTL"),
            ("PLATAFORMA", "Ftl", "PLATAFORMA")
        };
        foreach (var (source, mode, choice) in equipment)
        {
            var schema = WithEquipmentOptions(choice);
            using var result = JsonDocument.Parse(JsonSerializer.Serialize(
                BuildForTest(WithEquipment(source, mode), schema)));
            Assert.AreEqual(100000010, result.RootElement.GetProperty("inputs")
                .GetProperty("new_equipo").GetInt32(),
                $"{source} ({mode}) must resolve to {choice}");
        }
    }

    [DataTestMethod]
    [DataRow("26 pies", 100000012)]
    [DataRow("48 pies", 100000011)]
    [DataRow("48 ft", 100000011)]
    [DataRow("48 feet", 100000011)]
    [DataRow("53 pies", 100000010)]
    public void LengthOnlyFtlFootAliasesResolveToThePublishedDynamicsCategory(
        string source, int expectedChoice)
    {
        // Active new_equipo labels for length-only FTL equipment.
        var schema = WithEquipmentOptions("53\" FTL", "48\" FTL", "26\" FTL");
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(WithEquipment(source, "Ftl"), schema)));
        Assert.AreEqual(expectedChoice, result.RootElement.GetProperty("inputs")
            .GetProperty("new_equipo").GetInt32(), source);
    }

    [TestMethod]
    public void FootLengthAliasMustNotChangeTrailerSizeTonnageOrModality()
    {
        var schema = WithEquipmentOptions("53\" FTL", "24 TON FTL", "48\" FCL");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(WithEquipment("48 pies", "Ftl"), schema));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(WithEquipment("48 pies / 5 TON", "Ftl"),
                WithEquipmentOptions("48\" FTL")));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(WithEquipment("48 pies", "Fcl"),
                WithEquipmentOptions("48\" FTL")));
    }

    [TestMethod]
    public void FclCombinationCannotSilentlyCollapseToOneEquipment()
    {
        var schema = WithEquipmentOptions("40 STD FCL", "40 STD FCL - 40 HC FCL");
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(WithEquipment("40 STD - 40 HC", "Fcl"), schema)));
        Assert.AreEqual(100000011, result.RootElement.GetProperty("inputs")
            .GetProperty("new_equipo").GetInt32());
    }

    [TestMethod]
    public void DifferentTonnagesMustNotMapAutomaticallyEvenIfBothTrucksAre24Feet()
    {
        // QUO-00000-000476: Dynamics has a 7-ton/24-foot choice but no 5-ton/24-foot choice.
        var schema = WithEquipmentOptions("7 Ton / 24 pies", "24 TON FTL");
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(WithEquipment("24 pies / 5 TON", "Ftl"), schema));
        StringAssert.Contains(error.Message,
            "DynamicsQuoteSync:EquipmentMappings:24PIES5TONFTL");
    }

    [TestMethod]
    public void ApprovedEquipmentAliasResolvesOnlyToAnExistingDynamicsOption()
    {
        // After business review, an operator can explicitly authorize a catalog alias.
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DynamicsQuoteSync:EquipmentMappings:24PIES5TONFTL"] =
                    "7 Ton / 24 pies"
            }).Build();
        var schema = WithEquipmentOptions("7 Ton / 24 pies", "24 TON FTL");
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(WithEquipment("24 pies / 5 TON", "Ftl"), schema, config)));
        Assert.AreEqual(100000010, result.RootElement.GetProperty("inputs")
            .GetProperty("new_equipo").GetInt32());

        var invalidConfig = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DynamicsQuoteSync:EquipmentMappings:24PIES5TONFTL"] =
                    "Nonexistent Dataverse option"
            }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(WithEquipment("24 pies / 5 TON", "Ftl"),
                schema, invalidConfig));
    }


    [TestMethod]
    public void PublishedEnvironmentMappingsResolveFiveTonTwentyFourFootFTLWithoutChangingTotals()
    {
        // The business uses the existing seven-ton/24-foot category for a
        // five-ton requirement, but the original requested equipment remains
        // visible in the textual equipment description sent with the Quote.
        var schema = WithEquipmentOptions("7 Ton / 24 pies", "24 TON FTL");
        var snapshot = WithEquipment("24 pies / 5 TON", "Ftl");
        foreach (var environment in new[] { "Development", "Staging", "Production" })
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            string? filename = null;
            while (folder is not null)
            {
                var candidate = Path.Combine(folder.FullName,
                    "src", "Dhole.Pricing.Api", "appsettings." + environment + ".json");
                if (File.Exists(candidate))
                {
                    filename = candidate;
                    break;
                }
                folder = folder.Parent;
            }
            Assert.IsNotNull(filename, "Missing Pricing config for " + environment);
            using var stream = File.OpenRead(filename);
            var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
            using var mapped = JsonDocument.Parse(JsonSerializer.Serialize(
                BuildForTest(snapshot, schema, configuration)));
            var inputs = mapped.RootElement.GetProperty("inputs");
            Assert.AreEqual(100000010, inputs.GetProperty("new_equipo").GetInt32(),
                environment);
            Assert.AreEqual("2 x 24 pies / 5 TON",
                inputs.GetProperty("new_cantidadequipos").GetString(), environment);
            Assert.AreEqual(2400.25m, inputs.GetProperty("new_flete").GetDecimal(),
                environment);
            Assert.AreEqual(0m, inputs.GetProperty("new_monto9").GetDecimal(),
                environment);
        }
    }

    [TestMethod]
    public void DuplicateDataverseEquipmentChoicesNeverSelectAnArbitraryValue()
    {
        var schema = WithEquipmentOptions("40 HC FCL", "40HC FCL");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(WithEquipment("40 HC", "Fcl"), schema));
    }

    [TestMethod]
    public void UnapprovedOrIdtraMissingNeverProducesAPayload()
    {
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(
                Accepted.Replace("AcceptedByClient", "PendingApproval"), schema));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(
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
            BuildForTest(Accepted, schema)));
        var inputs = mapped.RootElement.GetProperty("inputs");
        Assert.AreEqual(2400.25m, inputs.GetProperty("new_flete").GetDecimal());
        Assert.AreEqual(0m, inputs.GetProperty("new_monto9").GetDecimal());
        Assert.IsFalse(inputs.TryGetProperty("msdyn_invoicesetuptotals", out _));
        Assert.AreEqual(TestDholeRateId.ToString("D"),
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
            """
            {"name":"customerid","target":"customerid_account@odata.bind","type":"string","dataverseType":"Customer","required":true,"options":[]},
              {"name":"new_idtra1","target":"new_idtra1_incident@odata.bind","type":"string","dataverseType":"Lookup","required":true,"options":[]},
              {"name":"msdyn_account","target":"msdyn_account_account@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
            {"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}
            """,
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, schema)));
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.IsFalse(inputs.TryGetProperty("customerid", out _));
        Assert.IsFalse(inputs.TryGetProperty("msdyn_account", out _));
        Assert.AreEqual("IDTRA999.2026-045", inputs.GetProperty("new_idtra1").GetString());
        Assert.IsFalse(inputs.TryGetProperty("customeridtype", out _));
        Assert.IsFalse(inputs.TryGetProperty("new_idtraasignado", out _));
    }

    [TestMethod]
    public void AcceptedQuoteDoesNotBindCustomerLookupsEvenWhenItsNameIsInDescription()
    {
        var schemaText = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            """
            {"name":"customerid","target":"customerid_account@odata.bind","type":"string","dataverseType":"Customer","required":true,"options":[]},
              {"name":"msdyn_account","target":"msdyn_account_account@odata.bind","type":"string","dataverseType":"Lookup","required":true,"options":[]},
              {"name":"description","target":"description","type":"string","dataverseType":"Memo","required":true,"options":[]},
            {"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}
            """,
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, schema)));
        var inputs = doc.RootElement.GetProperty("inputs");
        Assert.IsFalse(inputs.TryGetProperty("customerid", out _));
        Assert.IsFalse(inputs.TryGetProperty("customeridtype", out _));
        Assert.IsFalse(inputs.TryGetProperty("msdyn_account", out _));
        Assert.AreEqual("Oferta 40HC", inputs.GetProperty("name").GetString());
        Assert.AreEqual(
            "Cliente Prueba | QUO-2026-003 | Fcl | USD | IDTRA999.2026-045",
            inputs.GetProperty("description").GetString());
        Assert.AreEqual(2400.25m, inputs.GetProperty("new_flete").GetDecimal());
    }

    [TestMethod]
    public void DynamicsQuoteDescriptionPutsClientFirstAndKeepsOriginalDetails()
    {
        var schemaNode = JsonNode.Parse(Schema)!.AsObject();
        schemaNode["fields"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"description","target":"description","type":"string",
             "dataverseType":"Memo","required":true,"options":[]}
            """));
        var rate = JsonNode.Parse(Accepted)!.AsObject();
        rate["clientName"] = "  EL REINADO DE LAS LLANTAS  ";
        rate["rateCode"] = "QUO-00000-000233";
        rate["idtraNumber"] = "IDTRA999.12761";
        using var outgoing = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(rate.ToJsonString(),
                DynamicsQuotePayloadBuilder.ReadSchema(schemaNode.ToJsonString()))));
        Assert.AreEqual(
            "EL REINADO DE LAS LLANTAS | QUO-00000-000233 | Fcl | USD | IDTRA999.12761",
            outgoing.RootElement.GetProperty("inputs").GetProperty("description").GetString());

        // Older accepted snapshots without a customer must retain the
        // complete previous description without leading separators.
        rate.Remove("clientName");
        using var legacy = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(rate.ToJsonString(),
                DynamicsQuotePayloadBuilder.ReadSchema(schemaNode.ToJsonString()))));
        Assert.AreEqual("QUO-00000-000233 | Fcl | USD | IDTRA999.12761",
            legacy.RootElement.GetProperty("inputs").GetProperty("description").GetString());
    }

    [TestMethod]
    public void DynamicsQuoteNameUsesCompleteStoredPricingRateName()
    {
        const string fullName =
            "QUO-00000-000233 - Tarifa 1 x 40 High Cube - FOB - Qingdao, China To San Jose, Costa Rica Via Caldera - EL REINADO DE LAS LLANTAS";
        var accepted = JsonNode.Parse(Accepted)!.AsObject();
        accepted["rateCode"] = "QUO-00000-000233";
        accepted["rateName"] = fullName;
        accepted["clientName"] = "EL REINADO DE LAS LLANTAS";
        using var outgoing = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(accepted.ToJsonString(), DynamicsQuotePayloadBuilder.ReadSchema(Schema))));
        var inputs = outgoing.RootElement.GetProperty("inputs");
        Assert.AreEqual(fullName, inputs.GetProperty("name").GetString());
        Assert.IsFalse(inputs.TryGetProperty("customerid", out _));
        Assert.IsFalse(inputs.TryGetProperty("msdyn_account", out _));
        Assert.AreEqual(2400.25m, inputs.GetProperty("new_flete").GetDecimal());
    }

    [TestMethod]
    public void NewQuotesAlwaysSendExactResponseNotificationChoiceForCreateAndUpsert()
    {
        foreach (var operation in new[] { "create", "upsert" })
        {
            var schemaNode = JsonNode.Parse(Schema)!.AsObject();
            schemaNode["operation"] = operation;
            var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaNode.ToJsonString());
            using var outgoing = JsonDocument.Parse(JsonSerializer.Serialize(
                BuildForTest(Accepted, schema)));
            var inputs = outgoing.RootElement.GetProperty("inputs");
            Assert.AreEqual(100000000,
                inputs.GetProperty("new_notifrespuestacotizacion").GetInt32());
        }
    }

    [TestMethod]
    public void MissingOrIncorrectNotificationChoiceStopsQuoteBeforeWrite()
    {
        var node = JsonNode.Parse(Schema)!.AsObject();
        var fields = node["fields"]!.AsArray();
        var index = fields.ToList().FindIndex(x =>
            x?["target"]?.GetValue<string>() == "new_notifrespuestacotizacion");
        Assert.IsTrue(index >= 0);
        var notification = fields[index]!.DeepClone();
        fields.RemoveAt(index);
        var missing = Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(Accepted, DynamicsQuotePayloadBuilder.ReadSchema(node.ToJsonString())));
        StringAssert.Contains(missing.Message, "new_notifrespuestacotizacion");

        fields.Insert(index, notification);
        fields[index]!["type"] = "boolean";
        fields[index]!["dataverseType"] = "Boolean";
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(Accepted, DynamicsQuotePayloadBuilder.ReadSchema(node.ToJsonString())));

        fields[index]!["type"] = "integer";
        fields[index]!["dataverseType"] = "Picklist";
        fields[index]!["options"] = JsonNode.Parse(
            """[{"label":"Otro estado","value":100000001}]""");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(Accepted, DynamicsQuotePayloadBuilder.ReadSchema(node.ToJsonString())));
    }

    [TestMethod]
    public void UnrelatedBooleanNotificationIsNeverWritten()
    {
        var node = JsonNode.Parse(Schema)!.AsObject();
        node["fields"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"notifInicioCotizacion","target":"new_notif_iniciocotizacion",
             "type":"boolean","dataverseType":"Boolean","required":false,"options":[]}
            """));
        using var outgoing = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, DynamicsQuotePayloadBuilder.ReadSchema(node.ToJsonString()))));
        var inputs = outgoing.RootElement.GetProperty("inputs");
        Assert.AreEqual(100000000, inputs.GetProperty("new_notifrespuestacotizacion").GetInt32());
        Assert.IsFalse(inputs.TryGetProperty("notifInicioCotizacion", out _));
    }

    [TestMethod]
    public void IdtraMustBeMappedToAnAuthorizedField()
    {
        var schemaText = Schema.Replace(
            """{"name":"new_idtraasignado","target":"new_idtraasignado","type":"string","dataverseType":"String","required":false,"options":[]},""",
            "", StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(Accepted, schema));
    }

    [TestMethod]
    public void SingleMetadataChoiceIsUsedWhenOldDefaultIsNotAvailable()
    {
        var altered = Schema.Replace("100000001", "100000000", StringComparison.Ordinal)
            .Replace("100000003", "100000000", StringComparison.Ordinal)
            .Replace("100000004", "100000000", StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(altered);
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, schema)));
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
            BuildForTest(Accepted, schema));
        var options = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string,string?> {
                ["DynamicsQuoteSync:Choices:new_categoriatarifa"] = "100000002"
            }).Build();
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, schema, options)));
        Assert.AreEqual(100000002, parsed.RootElement.GetProperty("inputs")
            .GetProperty("new_categoriatarifa").GetInt32());
    }

    [TestMethod]
    public void UnknownEquipmentFailsInsteadOfPickingAnArbitraryCode()
    {
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(
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
            new[] { "IDTRA999.2026-00125" },
            DynamicsQuotePayloadBuilder.ParseIdtraNumbers(
                "IDTRA-2026-00125 // QUO-00000-000316"));
        CollectionAssert.AreEqual(
            new[] { "IDTRA999.12585" },
            DynamicsQuotePayloadBuilder.ParseIdtraNumbers(
                "IDTRA IDTRA999.12585-QUO 11228"));
        CollectionAssert.AreEqual(
            new[] { "IDTRA999.12585", "IDTRA999.12586" },
            DynamicsQuotePayloadBuilder.ParseIdtraNumbers(
                "IDTRA IDTRA999.12585-QUO 11228, IDTRA999.12586-QUO 11229 // QUO-00000-000352"));
        CollectionAssert.AreEqual(
            new[] { "IDTRA999.2026-00125" },
            DynamicsQuotePayloadBuilder.ParseIdtraNumbers(
                "IDTRA-2026-00125"));
    }

    [TestMethod]
    public void DynamicsLookupAlwaysUsesIdtra999PrefixAndNeverIncludesQuo()
    {
        var cases = new (string Input, string Expected)[]
        {
            ("IDTRA12589-QUO11098", "IDTRA999.12589"),
            ("IDTRA999.12589-QUO11098", "IDTRA999.12589"),
            ("IDTRA12589", "IDTRA999.12589"),
            ("12589", "IDTRA999.12589"),
            ("999.12589", "IDTRA999.12589"),
            ("IDTRA 12589", "IDTRA999.12589"),
            ("IDTRA IDTRA999.12589 // QUO-00000-000061", "IDTRA999.12589"),
            ("IDTRA-2026-00125 // QUO-123", "IDTRA999.2026-00125")
        };
        foreach (var (input, expected) in cases)
        {
            CollectionAssert.AreEqual(new[] { expected },
                DynamicsQuotePayloadBuilder.ParseIdtraNumbers(input), input);
            Assert.AreEqual(expected, IdtraNumberNormalizer.NormalizeCsvOrNull(input), input);
        }
        CollectionAssert.AreEqual(new[] { "IDTRA999.12589", "IDTRA999.12590" },
            DynamicsQuotePayloadBuilder.ParseIdtraNumbers(
                "IDTRA12589-QUO11098, 12590-QUO11099, IDTRA999.12589 // QUO-00000-000061"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuotePayloadBuilder.ParseIdtraNumbers("QUO11098"));
    }

    [TestMethod]
    public void ActiveQuoteLookupUsesCanonicalIdtraForExistingRawSnapshot()
    {
        var fields = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            """
            {"name":"new_idtra1","target":"new_IDTRA1@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
            {"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}
            """,
            StringComparison.Ordinal);
        var accepted = Accepted.Replace("IDTRA-2026-045", "IDTRA12589-QUO11098");
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(accepted,
                DynamicsQuotePayloadBuilder.ReadSchema(fields))));
        var inputs = result.RootElement.GetProperty("inputs");
        Assert.AreEqual("IDTRA999.12589", inputs.GetProperty("new_idtra1").GetString());
        Assert.AreEqual(TestDholeRateId.ToString("D"), inputs.GetProperty("new_idtraasignado").GetString());
    }

    [TestMethod]
    public void MultipleIdtrasFillTheirDistinctApprovedLookupSlots()
    {
        var schemaText = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            """
            {"name":"new_idtra1","target":"new_idtra1_incident@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
              {"name":"new_idtra2","target":"new_idtra2_incident@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
              {"name":"new_idtra3","target":"new_idtra3_incident@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
            {"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}
            """,
            StringComparison.Ordinal);
        var multiSnapshot = Accepted.Replace("IDTRA-2026-045",
            "IDTRA IDTRA999.12565, IDTRA999.12566 // QUO-00000-000316 // QUO-11229-Q4X8M7",
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(multiSnapshot, schema)));
        var inputs = result.RootElement.GetProperty("inputs");
        Assert.AreEqual("IDTRA999.12565", inputs.GetProperty("new_idtra1").GetString());
        Assert.AreEqual("IDTRA999.12566", inputs.GetProperty("new_idtra2").GetString());
        Assert.IsFalse(inputs.TryGetProperty("new_idtra3", out _));
        Assert.AreEqual(TestDholeRateId.ToString("D"),
            inputs.GetProperty("new_idtraasignado").GetString());
    }

    [TestMethod]
    public void MultipleIdtraNeverSilentlyDiscardsMissingLookupSlots()
    {
        var schemaText = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            """
            {"name":"new_idtra1","target":"new_idtra1_incident@odata.bind","type":"string","dataverseType":"Lookup","required":false,"options":[]},
            {"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}
            """,
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(
                Accepted.Replace("IDTRA-2026-045",
                    "IDTRA IDTRA999.12565, IDTRA999.12566 // QUO-00000-000316",
                    StringComparison.Ordinal), schema));
    }

    [TestMethod]
    public void MoreThanTenIdtraMapsFirstTenWithoutChangingAcceptedSnapshot()
    {
        // Regression for QUO-00000-000552: 23 distinct IDTRA, one duplicate,
        // and only the first ten slots are available in Dataverse.
        var references = new[]
        {
            "IDTRA999.12552", "IDTRA999.12553", "IDTRA999.12640",
            "IDTRA999.12667", "IDTRA999.12668", "IDTRA999.12669",
            "IDTRA999.12670", "IDTRA999.12671", "IDTRA999.12672",
            "IDTRA999.12673", "IDTRA999.12674", "IDTRA999.12675",
            "IDTRA999.12676", "IDTRA999.12677", "IDTRA999.12678",
            "IDTRA999.12685", "IDTRA999.12756", "IDTRA999.12757",
            "IDTRA999.12758", "IDTRA999.12759", "IDTRA999.12760",
            "IDTRA999.12773", "IDTRA999.12774"
        };
        var csv = references[0] + ", " + string.Join(", ", references);
        Assert.AreEqual(23, DynamicsQuotePayloadBuilder.ParseIdtraNumbers(csv).Length);

        const string anchor = """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""";
        var lookups = string.Join(",\n", Enumerable.Range(1, 10).Select(i =>
            "{\"name\":\"new_idtra" + i + "\",\"target\":\"new_idtra" + i +
            "_incident@odata.bind\",\"type\":\"string\",\"dataverseType\":\"Lookup\",\"required\":false,\"options\":[]}"));
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(
            Schema.Replace(anchor, lookups + ",\n" + anchor, StringComparison.Ordinal));
        var snapshot = Accepted.Replace("IDTRA-2026-045", csv, StringComparison.Ordinal);
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(snapshot, schema)));
        var inputs = result.RootElement.GetProperty("inputs");
        for (var i = 1; i <= 10; i++)
            Assert.AreEqual(references[i - 1],
                inputs.GetProperty("new_idtra" + i).GetString());
        Assert.IsFalse(inputs.TryGetProperty("new_idtra11", out _));
        var description = inputs.GetProperty("description").GetString()!;
        StringAssert.Contains(description, references[9]);
        Assert.IsFalse(description.Contains(references[10], StringComparison.Ordinal));
        using var source = JsonDocument.Parse(snapshot);
        Assert.AreEqual(csv, source.RootElement.GetProperty("idtraNumber").GetString());
        Assert.AreEqual(TestDholeRateId.ToString("D"),
            inputs.GetProperty("new_idtraasignado").GetString());
    }

    [TestMethod]
    public void AdditionalAmountsAndCurrencyMapWithoutDuplicatingSupplierCost()
    {
        const string extraFields = """
            {"name":"new_monto1","target":"new_monto1","type":"decimal","dataverseType":"Money","required":false,"options":[]},
            {"name":"new_monto2","target":"new_monto2","type":"decimal","dataverseType":"Money","required":false,"options":[]},
            {"name":"new_divisa","target":"new_divisa","type":"string","dataverseType":"String","required":false,"options":[]},
            """;
        var expanded = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            extraFields + "\n" +
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            StringComparison.Ordinal);
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, DynamicsQuotePayloadBuilder.ReadSchema(expanded))));
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
            BuildForTest(Accepted, schema));
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
            BuildForTest(
                Accepted.Replace("IDTRA-2026-045", csv, StringComparison.Ordinal), schema)));
        var inputs = result.RootElement.GetProperty("inputs");
        Assert.AreEqual("IDTRA999.10", inputs.GetProperty("new_idtra10").GetString());
        Assert.AreEqual(TestDholeRateId.ToString("D"),
            inputs.GetProperty("new_idtraasignado").GetString());
    }

    [TestMethod]
    public void CurrencyRequiresResolvedLiveDataverseId()
    {
        const string field = """
            {"name":"transactioncurrencyid","target":"transactioncurrencyid@odata.bind","type":"guid","dataverseType":"Lookup","required":false,"options":[]},
            """;
        var schemaText = Schema.Replace(
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            field + "\n" +
            """{"name":"new_cantidadequipos","target":"new_cantidadequipos","type":"string","dataverseType":"String","required":false,"options":[]}""",
            StringComparison.Ordinal);
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            BuildForTest(Accepted, schema));
        var usd = Guid.NewGuid();
        var crc = Guid.NewGuid();
        using var usdPayload = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(Accepted, schema, resolvedCurrencyId: usd)));
        using var crcPayload = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(
                Accepted.Replace("\"currencyCode\":\"USD\"", "\"currencyCode\":\"CRC\""),
                schema, resolvedCurrencyId: crc)));
        Assert.AreEqual(usd.ToString("D"),
            usdPayload.RootElement.GetProperty("inputs")
                .GetProperty("transactioncurrencyid").GetString());
        Assert.AreEqual(crc.ToString("D"),
            crcPayload.RootElement.GetProperty("inputs")
                .GetProperty("transactioncurrencyid").GetString());
        Assert.AreNotEqual(usd, crc);
    }

    [TestMethod]
    public void LclEquipmentQuantityUsesChargeableCbmInsteadOfZeroContainers()
    {
        var rate = Accepted.Replace("\"shipmentMode\":\"Fcl\"",
            "\"shipmentMode\":\"Lcl\"")
            .Replace("\"containerQuantity\":2",
                "\"containerQuantity\":0,\"chargeableQuantity\":12.64");
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(Schema);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(rate, schema)));
        Assert.AreEqual("12.64 CBM LCL", payload.RootElement.GetProperty("inputs")
            .GetProperty("new_cantidadequipos").GetString());
    }

    [TestMethod]
    public void CommercialTermsAreOmittedEvenWhenTemplateMarksInputsRequired()
    {
        const string commercialFields = """
            {"name":"new_noincluye","target":"new_noincluye","type":"string","dataverseType":"String","required":true,"options":[]},
            {"name":"new_tarifasujetaa","target":"new_tarifasujetaa","type":"string","dataverseType":"String","required":true,"options":[]},
            {"name":"new_tarifaincluye","target":"new_tarifaincluye","type":"string","dataverseType":"String","required":true,"options":[]},
            """;
        // Append approved commercial fields without changing the mandatory
        // Pricing quote schema or any of its other field mappings.
        var fields = JsonNode.Parse(Schema)!.AsObject()["fields"]!.AsArray();
        var extra = JsonNode.Parse("[" + commercialFields.Trim().TrimEnd(',') + "]")!.AsArray();
        foreach (var field in extra) fields.Add(field!.DeepClone());
        var schema = DynamicsQuotePayloadBuilder.ReadSchema(fields.Parent!.ToJsonString());
        var accepted = JsonNode.Parse(Accepted)!.AsObject();
        accepted["excludes"] = new string('X', 12000);
        accepted["subjectTo"] = new string('Y', 12000);
        accepted["includes"] = new string('Z', 12000);
        using var outbound = JsonDocument.Parse(JsonSerializer.Serialize(
            BuildForTest(accepted.ToJsonString(), schema)));
        var inputs = outbound.RootElement.GetProperty("inputs");
        Assert.IsFalse(inputs.TryGetProperty("new_noincluye", out _));
        Assert.IsFalse(inputs.TryGetProperty("new_tarifasujetaa", out _));
        Assert.IsFalse(inputs.TryGetProperty("new_tarifaincluye", out _));
        Assert.AreEqual("Oferta 40HC", inputs.GetProperty("name").GetString());
        Assert.AreEqual(2400.25m, inputs.GetProperty("new_flete").GetDecimal());
        Assert.AreEqual(12000, accepted["excludes"]!.GetValue<string>().Length);
    }
}

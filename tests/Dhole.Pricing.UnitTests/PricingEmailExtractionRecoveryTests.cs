using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class PricingEmailExtractionRecoveryTests
{
    [TestMethod]
    public void Recover_WwlNarrativeNac_MovesPodToPoeAndDefaults40Hc()
    {
        var rowId = Guid.NewGuid();
        var extraction = new DataExtractionFclPricingResult(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "correlation-id",
            new DataExtractionFclPricingSummary(1, 0, 0, 1, true),
            [
                new DataExtractionFclPricingRow(
                    rowId,
                    "AI Email",
                    1,
                    "Shanghai",
                    null,
                    "Caldera",
                    null,
                    "ONE",
                    null,
                    "Auto Spare Parts",
                    "USD",
                    21,
                    null,
                    new DateTime(2026, 8, 8),
                    new DateTime(2026, 8, 14),
                    6400m,
                    null,
                    null,
                    65m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Producto comercial: NAC",
                    "Invalid",
                    "{\"product\":\"NAC\"}",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
            ],
            [
                new DataExtractionFclPricingIssue(
                    Guid.NewGuid(),
                    rowId,
                    "missing_container_type",
                    "Falta equipo",
                    true,
                    "AI Email",
                    1,
                    "ContainerType",
                    null
                ),
            ],
            null,
            null
        );

        var recovered = PricingEmailExtractionRecovery.Recover(
            extraction,
            ImportSourceType.Email,
            "RV: CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG",
            "email-body.txt"
        );
        var row = recovered.Rows.Single();

        Assert.AreEqual("Caldera", row.PortOfExit);
        Assert.IsNull(row.DestinationPort);
        Assert.AreEqual("40HC", row.ContainerType);
        StringAssert.Contains(row.Remarks, "40HC");

        var mapped = StandardizedImportFclRateFactory.CreateRates(
            Guid.NewGuid(),
            ImportSourceType.Email,
            recovered,
            null
        );
        Assert.HasCount(1, mapped.Rates);
    }

    [TestMethod]
    public void Recover_EffectiveEtdSingleDate_CompletesValidityRange()
    {
        var rowId = Guid.NewGuid();
        var effectiveDate = new DateTime(2026, 8, 24);
        var extraction = new DataExtractionFclPricingResult(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "correlation-id",
            new DataExtractionFclPricingSummary(1, 1, 0, 0, false),
            [
                new DataExtractionFclPricingRow(
                    rowId,
                    "AI Email",
                    1,
                    "Shanghai",
                    "Caldera",
                    null,
                    "40HC",
                    "PIL",
                    null,
                    "General Cargo",
                    "USD",
                    18,
                    null,
                    effectiveDate,
                    null,
                    7800m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Effective ETD: 24-Aug",
                    "Valid",
                    "{\"Effective ETD\":\"24-Aug\"}",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
            ],
            [],
            null,
            null
        );

        var recovered = PricingEmailExtractionRecovery.Recover(
            extraction,
            ImportSourceType.Email,
            "RS rates update",
            "email-body.txt"
        );
        var row = recovered.Rows.Single();

        Assert.AreEqual(effectiveDate, row.ValidFrom);
        Assert.AreEqual(effectiveDate, row.ValidTo);
        StringAssert.Contains(row.Remarks, "Effective ETD");
    }

    [TestMethod]
    public void Recover_Pier17MiamiAir_RepairsRouteAmountModeAndStaleIssues()
    {
        var rowId = Guid.NewGuid();
        const string rawJson = """
            {
              "_dholeSource": {
                "subject": "Re: TARIFARIO AEREO PIER 17 MIAMI / OCTUBRE 2026",
                "originalFileName": "PIER17 MIAMI.pdf"
              },
              "extracted": {
                "Airline": "American Airlines",
                "MinimumRate": "75.00",
                "Flete +100": "2.65",
                "Flete +300": "2.45",
                "Flete +500": "2.30",
                "ServiceMode": "AIR_CONSOLIDATED"
              }
            }
            """;

        var extraction = new DataExtractionFclPricingResult(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "pier17-air-miami",
            new DataExtractionFclPricingSummary(1, 0, 0, 1, true),
            [
                new DataExtractionFclPricingRow(
                    rowId,
                    "PDF",
                    1,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    1,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Invalid",
                    rawJson,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                )
            ],
            [
                new DataExtractionFclPricingIssue(
                    Guid.NewGuid(),
                    rowId,
                    "missing_origin_port",
                    "Falta origen.",
                    true,
                    "PDF",
                    1,
                    "OriginPort",
                    null
                ),
                new DataExtractionFclPricingIssue(
                    Guid.NewGuid(),
                    rowId,
                    "missing_port_of_exit",
                    "Falta destino.",
                    true,
                    "PDF",
                    1,
                    "PortOfExit",
                    null
                ),
                new DataExtractionFclPricingIssue(
                    Guid.NewGuid(),
                    rowId,
                    "missing_rate_amount",
                    "Falta monto.",
                    true,
                    "PDF",
                    1,
                    "TotalSale",
                    null
                ),
                new DataExtractionFclPricingIssue(
                    Guid.NewGuid(),
                    rowId,
                    "missing_container_type",
                    "Falta modalidad.",
                    true,
                    "PDF",
                    1,
                    "ContainerType",
                    null
                )
            ],
            null,
            null
        );

        var recovered = PricingEmailExtractionRecovery.Recover(
            extraction,
            ImportSourceType.Email,
            "Re: TARIFARIO AEREO PIER 17 MIAMI / OCTUBRE 2026",
            "PIER17 MIAMI.pdf"
        );
        var row = recovered.Rows.Single();

        Assert.AreEqual("MIA", row.OriginPort);
        Assert.AreEqual("SJO", row.PortOfExit);
        Assert.AreEqual("AIR", row.ContainerType);
        Assert.AreEqual("American Airlines", row.Carrier);
        Assert.AreEqual("Pier17", row.Agent);
        Assert.AreEqual("USD", row.Currency);
        Assert.AreEqual(new DateTime(2026, 10, 1), row.ValidFrom);
        Assert.AreEqual(new DateTime(2026, 10, 31), row.ValidTo);
        Assert.AreEqual(2.65m, row.OceanFreight);
        StringAssert.Contains(row.Remarks, "+300: 2.45 USD");
        StringAssert.Contains(row.Remarks, "+500: 2.3 USD");

        var mapped = StandardizedImportFclRateFactory.CreateRates(
            Guid.NewGuid(),
            ImportSourceType.Email,
            recovered,
            null
        );

        Assert.HasCount(1, mapped.Rates);
        var rate = mapped.Rates.Single();
        Assert.AreEqual("MIA", rate.PolName);
        Assert.AreEqual("SJO", rate.PoeName);
        Assert.AreEqual("AIR", rate.ContainerTypeCode);
        Assert.AreEqual(2.65m, rate.OceanFreight);
        Assert.AreEqual(0, mapped.SkippedExtractionRowIds.Count);
    }

}

using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Contracts.Imports.Request;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class DataExtractionPricingImportMapperTests
{
    [TestMethod]
    public void ToApplicationResult_SpotRate_UsesUploadDayAndEnrichesComments()
    {
        var rowId = Guid.NewGuid();
        var sourceValidFrom = new DateTime(2026, 9, 20);
        var sourceValidTo = new DateTime(2026, 9, 30);
        const string rawJson = """
            {
              "Tipo Tarifa": "SPOT",
              "ETD": "26/09/2026",
              "Commodity": "Electrónicos"
            }
            """;

        var response = new ExtractedPricingDataRequest(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "corr-spot",
            new ExtractedPricingSummaryRequest(1, 0, 0, 1, true),
            [
                new ExtractedPricingRowRequest(
                    rowId,
                    "Tarifas",
                    2,
                    "SHANGHAI",
                    "BALBOA",
                    null,
                    "40HC",
                    "MAERSK",
                    null,
                    null,
                    "USD",
                    7,
                    36,
                    sourceValidFrom,
                    sourceValidTo,
                    6600m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Sujeto a espacio",
                    "Observación existente",
                    "Invalid",
                    rawJson
                )
            ],
            [
                new ExtractedPricingIssueRequest(
                    Guid.NewGuid(),
                    rowId,
                    "invalid_validity_range",
                    "Rango de vigencia inválido.",
                    true,
                    "Tarifas",
                    2,
                    "ValidTo",
                    null
                )
            ],
            null,
            null
        );

        var result = response.ToApplicationResult(
            Guid.NewGuid(),
            response.PricingImportId
        );
        var row = result.Rows.Single();
        var expectedDate = GetCostaRicaToday();

        Assert.AreEqual(expectedDate, row.ValidFrom);
        Assert.AreEqual(expectedDate, row.ValidTo);
        Assert.AreEqual("Electrónicos", row.Commodity);
        StringAssert.Contains(row.SpaceComment, "Sujeto a espacio");
        StringAssert.Contains(row.SpaceComment, "Observación existente");
        StringAssert.Contains(row.SpaceComment, "ETD: 26/09/2026");
        StringAssert.Contains(row.SpaceComment, "Commodity: Electrónicos");
        Assert.IsFalse(result.Issues.Single().IsBlocking);
    }

    [TestMethod]
    public void ToApplicationResult_NonSpotRate_PreservesSourceValidityAndBlockingIssue()
    {
        var rowId = Guid.NewGuid();
        var sourceValidFrom = new DateTime(2026, 9, 20);
        var sourceValidTo = new DateTime(2026, 9, 30);
        const string rawJson = """{ "Tipo Tarifa": "FAK", "ETD": "26/09/2026" }""";

        var response = new ExtractedPricingDataRequest(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "corr-non-spot",
            new ExtractedPricingSummaryRequest(1, 0, 0, 1, true),
            [
                new ExtractedPricingRowRequest(
                    rowId,
                    "Tarifas",
                    2,
                    "SHANGHAI",
                    "BALBOA",
                    null,
                    "40HC",
                    "MAERSK",
                    null,
                    null,
                    "USD",
                    7,
                    36,
                    sourceValidFrom,
                    sourceValidTo,
                    6600m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Comentario",
                    null,
                    "Invalid",
                    rawJson
                )
            ],
            [
                new ExtractedPricingIssueRequest(
                    Guid.NewGuid(),
                    rowId,
                    "invalid_validity_range",
                    "Rango de vigencia inválido.",
                    true,
                    "Tarifas",
                    2,
                    "ValidTo",
                    null
                )
            ],
            null,
            null
        );

        var result = response.ToApplicationResult(
            Guid.NewGuid(),
            response.PricingImportId
        );
        var row = result.Rows.Single();

        Assert.AreEqual(sourceValidFrom, row.ValidFrom);
        Assert.AreEqual(sourceValidTo, row.ValidTo);
        Assert.AreEqual("Comentario", row.SpaceComment);
        Assert.IsTrue(result.Issues.Single().IsBlocking);
    }

    [TestMethod]
    public void ToApplicationResult_AirConsolidated_PreservesAirMetadataAndFallbackCarrier()
    {
        var rowId = Guid.NewGuid();
        const string rawJson = """
            {
              "TariffMode": "AIR",
              "ServiceMode": "AIR_CONSOLIDATED",
              "RateBasis": "KG/VOL",
              "MinimumRate": "150",
              "KgPerCbm": "167",
              "AirlineRoute": "PVG-SEA-ATL-SJO"
            }
            """;

        var response = new ExtractedPricingDataRequest(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "corr-air",
            new ExtractedPricingSummaryRequest(1, 1, 0, 0, false),
            [
                new ExtractedPricingRowRequest(
                    rowId,
                    "AIR - normalizado",
                    1,
                    "PVG",
                    "SJO",
                    null,
                    "AIR",
                    null,
                    null,
                    "General Cargo",
                    "USD",
                    null,
                    6,
                    new DateTime(2026, 9, 21),
                    new DateTime(2026, 9, 28),
                    6.37m,
                    null,
                    null,
                    null,
                    6.37m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Valid",
                    rawJson
                )
            ],
            [],
            null,
            null
        );

        var result = response.ToApplicationResult(Guid.NewGuid(), response.PricingImportId);
        var row = result.Rows.Single();

        Assert.AreEqual("AIR", row.ContainerType);
        Assert.AreEqual("Aéreo Consolidado", row.Carrier);
        Assert.AreEqual(6.37m, row.OceanFreight);
        StringAssert.Contains(row.SpaceComment, "AIR_CONSOLIDATED");
        StringAssert.Contains(row.SpaceComment, "KG/VOL");
        StringAssert.Contains(row.SpaceComment, "Mínimo: 150");
        StringAssert.Contains(row.SpaceComment, "1 CBM = 167 KG");
        StringAssert.Contains(row.SpaceComment, "PVG-SEA-ATL-SJO");
    }

    private static DateTime GetCostaRicaToday()
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).Date;
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTime.UtcNow.Date;
        }
        catch (InvalidTimeZoneException)
        {
            return DateTime.UtcNow.Date;
        }
    }
}

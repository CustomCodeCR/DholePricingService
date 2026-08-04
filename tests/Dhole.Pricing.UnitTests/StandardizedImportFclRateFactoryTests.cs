using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class StandardizedImportFclRateFactoryTests
{
    [TestMethod]
    public void CreateRates_WhenPodIsMissing_KeepsPoeIndependentAndCreatesPendingPod()
    {
        var rowId = Guid.NewGuid();
        var extraction = new DataExtractionFclPricingResult(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test-correlation",
            new DataExtractionFclPricingSummary(1, 1, 0, 0, false),
            [
                new DataExtractionFclPricingRow(
                    rowId,
                    "Rates",
                    2,
                    "SHANGHAI",
                    "CALDERA",
                    null,
                    "40HC",
                    "MAERSK",
                    "WWL",
                    "General",
                    "USD",
                    7,
                    22,
                    DateTime.UtcNow.Date,
                    DateTime.UtcNow.Date.AddDays(30),
                    1200m,
                    100m,
                    75m,
                    25m,
                    1400m,
                    1600m,
                    200m,
                    12.5m,
                    null,
                    null,
                    "Valid",
                    "{}",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                )
            ],
            [],
            null,
            null
        );

        var result = StandardizedImportFclRateFactory.CreateRates(
            Guid.NewGuid(),
            ImportSourceType.Email,
            extraction,
            null
        );

        var rate = result.Rates.Single();
        Assert.AreEqual("CALDERA", rate.PoeName);
        Assert.AreEqual("Por asignar", rate.PodName);
        Assert.AreEqual("PENDING", rate.PodCode);
        Assert.AreNotEqual(rate.PodId, rate.PoeId);
    }

    [TestMethod]
    public void CreateRates_WhenSomeConfigReferencesAreMissing_PreservesKnownReferencesAndRawValues()
    {
        var rowId = Guid.NewGuid();
        var polReference = Reference("pol", "SHA", "shanghai", "Shanghai");
        var podReference = Reference("pod", "CALDERA", "puerto-caldera", "Puerto Caldera");
        var carrierReference = Reference("carriers", "MSC", "msc", "Mediterranean Shipping Company");
        var containerReference = Reference("container-types", "40HC", "40hc", "40 High Cube");
        var currencyReference = Reference("currencies", "USD", "usd", "Dólar estadounidense");

        var extraction = new DataExtractionFclPricingResult(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test-partial-config",
            new DataExtractionFclPricingSummary(1, 0, 1, 0, true),
            [
                new DataExtractionFclPricingRow(
                    rowId,
                    "Rates",
                    2,
                    "SHANGHAI",
                    "MOIN",
                    "CALDERA",
                    "40HC",
                    "MSC",
                    null,
                    "General",
                    "USD",
                    14,
                    28,
                    DateTime.UtcNow.Date,
                    DateTime.UtcNow.Date.AddDays(30),
                    6210m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "RequiresReview",
                    "{}",
                    polReference,
                    null,
                    podReference,
                    containerReference,
                    carrierReference,
                    null,
                    currencyReference
                )
            ],
            [
                new DataExtractionFclPricingIssue(
                    Guid.NewGuid(),
                    rowId,
                    "unknown_port_of_exit",
                    "El POE no coincide con Config.",
                    true,
                    "Rates",
                    2,
                    "PortOfExit",
                    "MOIN"
                )
            ],
            null,
            null
        );

        var result = StandardizedImportFclRateFactory.CreateRates(
            Guid.NewGuid(),
            ImportSourceType.Email,
            extraction,
            null
        );

        var rate = result.Rates.Single();
        Assert.AreEqual(0, result.SkippedExtractionRowIds.Count);
        Assert.AreEqual(polReference.Id, rate.PolId);
        Assert.AreEqual("Shanghai", rate.PolName);
        Assert.AreEqual("MOIN", rate.PoeName);
        Assert.AreEqual(podReference.Id, rate.PodId);
        Assert.AreEqual("Puerto Caldera", rate.PodName);
        Assert.AreEqual(carrierReference.Id, rate.CarrierId);
        Assert.AreEqual(containerReference.Id, rate.ContainerTypeId);
        Assert.AreEqual(currencyReference.Id, rate.CurrencyId);
        Assert.AreEqual("Por asignar", rate.AgentName);
    }

    [TestMethod]
    public void CreateRates_WhenCurrencyIsMissing_DefaultsToUsdAndDoesNotSkipRow()
    {
        var rowId = Guid.NewGuid();
        var extraction = new DataExtractionFclPricingResult(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test-default-usd",
            new DataExtractionFclPricingSummary(1, 0, 0, 1, true),
            [
                new DataExtractionFclPricingRow(
                    rowId,
                    "Rates",
                    2,
                    "Shanghai",
                    "Moin",
                    null,
                    "40HC",
                    "MSC",
                    "RS",
                    null,
                    null,
                    14,
                    28,
                    new DateTime(2026, 8, 1),
                    new DateTime(2026, 8, 31),
                    6210m,
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
                    "{}",
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
                    "missing_currency",
                    "La fila no tiene moneda.",
                    true,
                    "Rates",
                    2,
                    "Currency",
                    null
                )
            ],
            null,
            null
        );

        var result = StandardizedImportFclRateFactory.CreateRates(
            Guid.NewGuid(),
            ImportSourceType.Email,
            extraction,
            null
        );

        var rate = result.Rates.Single();
        Assert.AreEqual(0, result.SkippedExtractionRowIds.Count);
        Assert.AreEqual("USD", rate.CurrencyCode);
        Assert.AreEqual("USD", rate.CurrencyName);
    }

    [TestMethod]
    public void CreateRates_WhenEmailAiPlacedDischargePortInPod_RecoversItAsPoe()
    {
        var rowId = Guid.NewGuid();
        var extraction = new DataExtractionFclPricingResult(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test-email-pod-as-poe",
            new DataExtractionFclPricingSummary(1, 0, 0, 1, true),
            [
                new DataExtractionFclPricingRow(
                    rowId,
                    "AI Email",
                    1,
                    "SHANGHAI",
                    null,
                    "CALDERA",
                    "40HC",
                    "ONE",
                    "WWL",
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
                    "POD marítimo informado por el correo.",
                    "Invalid",
                    "{}",
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
                    "missing_port_of_exit",
                    "La fila no tiene POE.",
                    true,
                    "AI Email",
                    1,
                    "PortOfExit",
                    null
                )
            ],
            null,
            null
        );

        var result = StandardizedImportFclRateFactory.CreateRates(
            Guid.NewGuid(),
            ImportSourceType.Email,
            extraction,
            null
        );

        var rate = result.Rates.Single();
        Assert.AreEqual("CALDERA", rate.PoeName);
        Assert.AreEqual("Por asignar", rate.PodName);
        Assert.AreEqual("PENDING", rate.PodCode);
        Assert.AreEqual(6400m, rate.OceanFreight);
        Assert.AreEqual(0, result.SkippedExtractionRowIds.Count);
    }

    private static DataExtractionCatalogReference Reference(
        string group,
        string code,
        string slug,
        string name
    ) => new(Guid.NewGuid(), group, code, slug, name, name);
}

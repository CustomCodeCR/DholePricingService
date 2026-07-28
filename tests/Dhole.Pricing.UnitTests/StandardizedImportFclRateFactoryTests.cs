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
        Assert.AreEqual(string.Empty, rate.PodName);
        Assert.AreEqual(string.Empty, rate.PodCode);
        Assert.AreEqual(Guid.Empty, rate.PodId);
        Assert.IsFalse(rate.HasConfigConcordance);
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
        Assert.AreEqual(Guid.Empty, rate.PoeId);
        Assert.AreEqual(podReference.Id, rate.PodId);
        Assert.AreEqual("Puerto Caldera", rate.PodName);
        Assert.AreEqual(carrierReference.Id, rate.CarrierId);
        Assert.AreEqual(containerReference.Id, rate.ContainerTypeId);
        Assert.AreEqual(currencyReference.Id, rate.CurrencyId);
        Assert.AreEqual(string.Empty, rate.AgentName);
        Assert.AreEqual(Guid.Empty, rate.AgentId);
        Assert.IsFalse(rate.HasConfigConcordance);
    }

    [TestMethod]
    public void CreateRates_DoesNotInventCatalogIdsForUnknownValues()
    {
        var rowId = Guid.NewGuid();
        var extraction = new DataExtractionFclPricingResult(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test-no-invented-ids",
            new DataExtractionFclPricingSummary(1, 0, 1, 0, true),
            [
                new DataExtractionFclPricingRow(
                    rowId,
                    "Rates",
                    2,
                    "Unknown POL",
                    "Unknown POE",
                    null,
                    "40SV",
                    "Unknown Carrier",
                    "Signature Company",
                    null,
                    "USD",
                    10,
                    null,
                    DateTime.UtcNow.Date,
                    DateTime.UtcNow.Date.AddDays(30),
                    1500m,
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

        var rate = StandardizedImportFclRateFactory.CreateRates(
            Guid.NewGuid(),
            ImportSourceType.Email,
            extraction,
            null
        ).Rates.Single();

        Assert.AreEqual(Guid.Empty, rate.ImportProfileId);
        Assert.AreEqual(Guid.Empty, rate.PolId);
        Assert.AreEqual(Guid.Empty, rate.PoeId);
        Assert.AreEqual(Guid.Empty, rate.PodId);
        Assert.AreEqual(Guid.Empty, rate.CarrierId);
        Assert.AreEqual(Guid.Empty, rate.AgentId);
        Assert.AreEqual(Guid.Empty, rate.ContainerTypeId);
        Assert.AreEqual(Guid.Empty, rate.CurrencyId);
        Assert.AreEqual("Unknown POL", rate.PolName);
        Assert.AreEqual("Signature Company", rate.AgentName);
    }

    private static DataExtractionCatalogReference Reference(
        string group,
        string code,
        string slug,
        string name
    ) => new(Guid.NewGuid(), group, code, slug, name, name);
}

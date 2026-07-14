using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class StandardizedImportFclRateFactoryTests
{
    [TestMethod]
    public void CreateRates_WhenPortOfExitIsMissing_UsesDestinationPortForPoe()
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
                    "   ",
                    "CALDERA",
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
        Assert.AreEqual(rate.PodName, rate.PoeName);
        Assert.AreEqual(rate.PodCode, rate.PoeCode);
    }
}

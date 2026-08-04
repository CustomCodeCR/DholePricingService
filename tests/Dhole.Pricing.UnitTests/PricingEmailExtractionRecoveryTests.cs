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
}

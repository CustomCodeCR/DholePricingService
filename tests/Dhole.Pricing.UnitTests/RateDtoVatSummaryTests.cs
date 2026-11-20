using System.Reflection;
using Dhole.Pricing.Application.Features.Rates.CreateRate;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateDtoVatSummaryTests
{
    [DataTestMethod]
    [DataRow("USD", "CUR-2026-001", 39.65)]
    [DataRow("CRC", "CUR-2026-002", 39.65)]
    [DataRow("Colón costarricense", "CUR-2026-002", 39.65)]
    public void OpaqueCatalogCurrency_SummarizesTaxFromCommercialLines(
        string currencyName, string catalogCode, double expectedTax)
    {
        var rate = RateHeader.Create(
            rateCode: "QUO-TEST-VAT", sourceImportFclRateId: null,
            agentId: Guid.NewGuid(), agentName: "Agente", agentCode: "AG",
            carrierId: Guid.NewGuid(), carrierName: "Proveedor", carrierCode: "PR",
            polId: Guid.NewGuid(), polName: "Origen", polCode: "ORG",
            poeId: Guid.NewGuid(), poeName: "Destino", poeCode: "DST",
            podId: null, podName: null, podCode: null,
            containerTypeId: Guid.NewGuid(), containerTypeName: "40HC", containerTypeCode: "40HC",
            incotermId: Guid.NewGuid(), incotermName: "FOB", incotermCode: "FOB",
            currencyId: Guid.NewGuid(), currencyName: "USD", currencyCode: "USD",
            freeDays: 0, validFrom: DateTime.UtcNow.Date, validTo: DateTime.UtcNow.Date.AddDays(1),
            containerQuantity: 1, clientName: "Prueba", idtraNumber: null, quoNumber: null,
            includes: null, subjectTo: null, excludes: null, transitTime: null,
            rateType: RateType.Spot, createdBy: null);

        var taxed = rate.AddRateDetail(rate.Id, null, "Carrusel",
            CostDetailType.DestinationCharge, CostType.Fixed, ChargeBasis.PerContainer,
            Guid.NewGuid(), currencyName, catalogCode, 250m, 305m, null, 1m, null);
        taxed.ConfigureDestinationTax(true, 13m);

        // An unmarked line must not add tax to the header.
        rate.AddRateDetail(rate.Id, null, "Release",
            CostDetailType.Documentation, CostType.Fixed, ChargeBasis.PerShipment,
            Guid.NewGuid(), currencyName, catalogCode, 75m, 75m, null, 1m, null);

        rate.SetAmounts(null);

        // RateMappings is internal; exercise the real API projection rather than
        // building a DTO by hand or simulating tax calculations in the test.
        var mappings = typeof(CreateRateCommandHandler).Assembly.GetType(
            "Dhole.Pricing.Application.Features.Rates.RateMappings", throwOnError: true)!;
        var toDto = mappings.GetMethod("ToDto",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var dto = (RateDto)toDto.Invoke(null, [rate])!;

        var expected = Convert.ToDecimal(expectedTax);
        if (currencyName == "USD")
        {
            Assert.AreEqual(expected, dto.TotalTaxUsd);
            Assert.AreEqual(0m, dto.TotalTaxCrc);
            Assert.AreEqual(dto.TotalSaleUsd + expected, dto.TotalSaleWithTaxUsd);
        }
        else
        {
            Assert.AreEqual(expected, dto.TotalTaxCrc);
            Assert.AreEqual(0m, dto.TotalTaxUsd);
            Assert.AreEqual(dto.TotalSaleCrc + expected, dto.TotalSaleWithTaxCrc);
        }
        Assert.AreEqual(expected, dto.RateDetails.Single(x => x.Name == "Carrusel").CommercialTaxAmount);
        Assert.AreEqual(0m, dto.RateDetails.Single(x => x.Name == "Release").CommercialTaxAmount);
    }
}

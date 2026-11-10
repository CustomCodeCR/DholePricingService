using System.Reflection;
using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class LegacyQuoteCatalogTariffVisibilityTests
{
    [TestMethod]
    public void HideTariffClients_ExcludesAnyCapitalizationOrPosition_ButKeepsOtherNames()
    {
        var candidates = new[]
        {
            NewRate("TARIFARIO XINGANG"),
            NewRate("CLIENTE TARIFARIO NAVIERA"),
            NewRate("cliente tarifario exterior"),
            NewRate("REINADO DE LAS LLANTAS"),
            NewRate(null),
        }.AsQueryable();

        var method = typeof(RateEndpoints).GetMethod(
            "ExcludeLegacyTariffClients", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "The catalog must filter TARIFARIO before server pagination.");
        var visible = (IQueryable<RateHeader>)method.Invoke(null, [candidates])!;
        var names = visible.Select(x => x.ClientName).ToArray();

        Assert.AreEqual(2, names.Length);
        Assert.IsTrue(names.Contains("REINADO DE LAS LLANTAS"));
        Assert.IsTrue(names.Contains(null));
    }

    private static RateHeader NewRate(string? clientName)
    {
        var today = DateTime.UtcNow.Date;
        return RateHeader.Create(
            rateCode: "QUO-A7K2P-9X4M8Q",
            sourceImportFclRateId: null,
            agentId: Guid.NewGuid(), agentName: "Agente", agentCode: "AGT",
            carrierId: Guid.NewGuid(), carrierName: "Naviera", carrierCode: "CAR",
            polId: Guid.NewGuid(), polName: "Shanghai", polCode: "CNSHA",
            poeId: Guid.NewGuid(), poeName: "Caldera", poeCode: "CRCAL",
            podId: Guid.NewGuid(), podName: "San José", podCode: "CRSJO",
            containerTypeId: Guid.NewGuid(), containerTypeName: "40HC", containerTypeCode: "40HC",
            incotermId: null, incotermName: null, incotermCode: null,
            currencyId: Guid.NewGuid(), currencyName: "Dólar", currencyCode: "USD",
            freeDays: 7, validFrom: today, validTo: today.AddDays(30),
            containerQuantity: 1, clientName: clientName,
            idtraNumber: null, quoNumber: "QUO-00000-000015",
            includes: null, subjectTo: null, excludes: null,
            transitTime: "20 días", rateType: RateType.Tariff, createdBy: null);
    }
}

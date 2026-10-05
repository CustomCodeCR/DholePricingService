using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Imports.Services;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ImportFclRateTransportNormalizationTests
{
    [TestMethod]
    public void Create_LclWithGenericModeCarrier_PreservesMaritimeColoaderAndClearsFakeCarrier()
    {
        var rate = Create(
            container: new CatalogSnapshot(
                Guid.Parse("f4d19764-7556-2a0d-9222-42d7b48d00d8"),
                "LCL",
                "LCL",
                "lcl"
            ),
            carrier: new CatalogSnapshot(
                Guid.Parse("79fc472e-e4df-d1c9-14b8-2e82b9996801"),
                "AÉREO",
                "AEREO",
                "aereo"
            ),
            rawDataJson: """{"Raw":{"TariffMode":"LCL","RateBasis":"W/M"}}"""
        );

        Assert.AreEqual(ImportedShipmentMode.LclColoader, rate.ShipmentMode);
        Assert.AreEqual("LCL", rate.ContainerTypeCode);
        Assert.AreEqual("Por asignar", rate.CarrierName);
        Assert.AreEqual("PORASIGNAR", rate.CarrierCode);
    }

    [TestMethod]
    public void Create_AirWithRealAirline_PreservesAirline()
    {
        var airlineId = Guid.NewGuid();
        var rate = Create(
            container: new CatalogSnapshot(
                Guid.Parse("321ae516-76a1-10ed-6d98-2117496f8ff4"),
                "AIR",
                "AIR",
                "air"
            ),
            carrier: new CatalogSnapshot(
                airlineId,
                "AMERICAN AIRLINES",
                "AA",
                "american-airlines"
            ),
            rawDataJson: """{"Raw":{"TariffMode":"AIR","RateBasis":"KG/VOL"}}"""
        );

        Assert.AreEqual(ImportedShipmentMode.AirLclColoader, rate.ShipmentMode);
        Assert.AreEqual("AIR", rate.ContainerTypeCode);
        Assert.AreEqual(airlineId, rate.CarrierId);
        Assert.AreEqual("AMERICAN AIRLINES", rate.CarrierName);
    }

    [TestMethod]
    public void Create_Fcl_PreservesContainerAndCarrier()
    {
        var carrierId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var rate = Create(
            container: new CatalogSnapshot(containerId, "40 High Cube", "40HC", "40-high-cube"),
            carrier: new CatalogSnapshot(carrierId, "MAERSK", "MAERSK", "maersk"),
            rawDataJson: """{"Raw":{"TariffMode":"FCL","Equipo":"40HC"}}"""
        );

        Assert.AreEqual(ImportedShipmentMode.Fcl, rate.ShipmentMode);
        Assert.AreEqual(containerId, rate.ContainerTypeId);
        Assert.AreEqual("40HC", rate.ContainerTypeCode);
        Assert.AreEqual(carrierId, rate.CarrierId);
        Assert.AreEqual("MAERSK", rate.CarrierName);
    }

    private static ImportFclRates Create(
        CatalogSnapshot container,
        CatalogSnapshot carrier,
        string rawDataJson
    ) =>
        ImportFclRates.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ImportSourceType.Email,
            Snapshot("Profile", "PROFILE"),
            Snapshot("Origin", "POL"),
            Snapshot("Destination", "POE"),
            Snapshot("Pending destination", "PENDING"),
            carrier,
            Snapshot("Pier17", "PIER17"),
            container,
            Snapshot("USD", "USD"),
            commodity: null,
            spaceComment: null,
            oceanFreight: 100m,
            originCharges: null,
            destinationCharges: null,
            surcharges: null,
            totalCost: 100m,
            totalSale: null,
            profit: null,
            margin: null,
            freeDays: 0,
            transitDays: null,
            validFrom: new DateTime(2026, 10, 5),
            validTo: new DateTime(2026, 10, 31),
            rawDataJson,
            createdBy: null
        );

    private static CatalogSnapshot Snapshot(string name, string code) =>
        new(Guid.NewGuid(), name, code, code.ToLowerInvariant());
}

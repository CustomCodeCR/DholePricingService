using Dhole.Pricing.Application.Features.Rates;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateCargoProfileFactoryTests
{
    [TestMethod]
    public void HeightAtSeventyInches_ForcesNonStackableAndBillsAt266Cm()
    {
        var profile = RateCargoProfileFactory.Create(
            ShipmentMode.Lcl,
            500m,
            [new RateCargoLineCommandItem("Caja alta", 1, 1, 0m, 100m, 100m, 177.8m, true)],
            0, 0, 0m, 0m);

        Assert.AreEqual(1.778m, profile.TotalVolumeCbm);
        Assert.AreEqual(2.66m, profile.ChargeableVolumeCbm);

        var line = RateCargoProfileFactory.Deserialize(profile.CargoLinesJson).Single();
        Assert.AreEqual(false, line.IsStackable);
        Assert.AreEqual(2.66m, line.BillableVolumeCbm);
        Assert.AreEqual(0.882m, line.DeadSpaceCbm);
    }

    [TestMethod]
    public void ManualNonStackableBelowThreshold_UsesMinimumBillableHeight()
    {
        var profile = RateCargoProfileFactory.Create(
            ShipmentMode.Lcl,
            500m,
            [new RateCargoLineCommandItem("Caja manual", 1, 1, 0m, 120m, 100m, 150m, false)],
            0, 0, 0m, 0m);

        Assert.AreEqual(1.8m, profile.TotalVolumeCbm);
        Assert.AreEqual(3.192m, profile.ChargeableVolumeCbm);

        var line = RateCargoProfileFactory.Deserialize(profile.CargoLinesJson).Single();
        Assert.AreEqual(false, line.IsStackable);
        Assert.AreEqual(1.392m, line.DeadSpaceCbm);
    }

    [TestMethod]
    public void MultipleCargoLines_SumPhysicalAndBillableVolumeIndependently()
    {
        var profile = RateCargoProfileFactory.Create(
            ShipmentMode.Lcl,
            500m,
            [
                new RateCargoLineCommandItem("2 cajas", 2, 2, 0m, 100m, 100m, 100m, true),
                new RateCargoLineCommandItem("1 tarima no estibable", 1, 1, 0m, 100m, 100m, 150m, false),
            ],
            0, 0, 0m, 0m);

        Assert.AreEqual(3.5m, profile.TotalVolumeCbm);
        Assert.AreEqual(4.66m, profile.ChargeableVolumeCbm);
        Assert.AreEqual(3, profile.TotalPackages);
    }

    [TestMethod]
    public void Ltl_HeightAbove270Cm_IsRejected()
    {
        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            RateCargoProfileFactory.Create(
                ShipmentMode.Ltl,
                330m,
                [new RateCargoLineCommandItem("Carga terrestre alta", 1, 1, 0m, 100m, 100m, 270.01m, false)],
                0, 0, 0m, 0m,
                "53"
            ));

        StringAssert.Contains(exception.Message, "270 cm");
    }

    [TestMethod]
    public void MaritimeLcl_40HcHeightAbove269Cm_IsRejected()
    {
        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            RateCargoProfileFactory.Create(
                ShipmentMode.Lcl,
                500m,
                [new RateCargoLineCommandItem("Carga marítima alta", 1, 1, 0m, 100m, 100m, 269.01m, false)],
                0, 0, 0m, 0m,
                "40HC"
            ));

        StringAssert.Contains(exception.Message, "269 cm");
        StringAssert.Contains(exception.Message, "40HC");
    }

    [TestMethod]
    public void AirLikeLclEquipment_DoesNotUseMaritimeHeightLimit()
    {
        var profile = RateCargoProfileFactory.Create(
            ShipmentMode.Lcl,
            500m,
            [new RateCargoLineCommandItem("ULD alto", 1, 1, 0m, 100m, 100m, 300m, false)],
            0, 0, 0m, 0m,
            "ULD"
        );

        Assert.AreEqual(3m, profile.TotalVolumeCbm);
    }

    [TestMethod]
    public void Ltl_DefaultWeightFactor_Is330KgPerCbm()
    {
        var profile = RateCargoProfileFactory.Create(
            ShipmentMode.Ltl,
            0m,
            [new RateCargoLineCommandItem("LTL", 1, 1, 330m, 100m, 100m, 100m, true)],
            0, 0, 0m, 0m);

        Assert.AreEqual(330m, profile.KgPerCbm);
    }
}

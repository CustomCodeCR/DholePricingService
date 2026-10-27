using Dhole.Pricing.Application.Features.Rates;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateCargoPalletSemanticsTests
{
    [TestMethod]
    public void Lcl_ThreePiecesOnOnePallet_UsesOnePalletVolumeAndPreservesDeclaredPieces()
        => VerifyPallets(ShipmentMode.Lcl, 1, 3.003m);

    [TestMethod]
    public void Ltl_ThreePiecesOnOnePallet_UsesOnePalletVolumeAndPreservesDeclaredPieces()
        => VerifyPallets(ShipmentMode.Ltl, 1, 3.003m);

    [TestMethod]
    public void Lcl_ThreePhysicalPallets_MultiplyVolumeByThree()
        => VerifyPallets(ShipmentMode.Lcl, 3, 9.009m);

    [TestMethod]
    public void Ltl_ThreePhysicalPallets_MultiplyVolumeByThree()
        => VerifyPallets(ShipmentMode.Ltl, 3, 9.009m);

    [TestMethod]
    public void LooseBoxes_UsePackageCountButNotPalletCount()
    {
        var profile = RateCargoProfileFactory.Create(
            ShipmentMode.Lcl, 500m,
            [new RateCargoLineCommandItem("Cajas", 3, 0, 3500m, 165m, 140m, 130m, true, "Box")],
            3, 0, 3500m, 0m
        );

        Assert.AreEqual(3, profile.TotalPackages);
        Assert.AreEqual(0, profile.TotalPallets);
        Assert.AreEqual(9.009m, profile.TotalVolumeCbm);
        Assert.AreEqual("Box", RateCargoProfileFactory.Deserialize(profile.CargoLinesJson).Single().UnitType);
    }

    private static void VerifyPallets(ShipmentMode mode, int palletQuantity, decimal expectedCbm)
    {
        var profile = RateCargoProfileFactory.Create(
            mode, mode == ShipmentMode.Ltl ? 330m : 500m,
            [new RateCargoLineCommandItem("Tarima con carretillas", 0, palletQuantity, 3500m, 165m, 140m, 130m, true, "Pallet")],
            3, palletQuantity, 3500m, 0m
        );

        Assert.AreEqual(3, profile.TotalPackages);
        Assert.AreEqual(palletQuantity, profile.TotalPallets);
        Assert.AreEqual(3500m, profile.TotalWeightKg); // Peso total: nunca × cantidad.
        Assert.AreEqual(expectedCbm, profile.TotalVolumeCbm);
        var line = RateCargoProfileFactory.Deserialize(profile.CargoLinesJson).Single();
        Assert.AreEqual("Pallet", line.UnitType);
        Assert.AreEqual(expectedCbm, line.VolumeCbm);
    }
}

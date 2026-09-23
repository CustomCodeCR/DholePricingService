using Dhole.Pricing.Domain.Imports.Services;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ImportShipmentModeClassifierTests
{
    [TestMethod]
    public void Classify_WhenContainerIsLcl_IgnoresConflictingRawFclMode()
    {
        const string raw = """
        {
          "Raw": {
            "Equipo": "LCL",
            "TariffMode": "FCL",
            "Ocean Freight": "30",
            "Surcharges": "78"
          }
        }
        """;

        var mode = ImportShipmentModeClassifier.Classify(
            "LCL",
            "LCL",
            "LCL",
            "lcl",
            raw);

        Assert.AreEqual(ImportedShipmentMode.Lcl, mode);
    }

    [TestMethod]
    public void Classify_WhenContainerIs40Hc_ReturnsFcl()
    {
        var mode = ImportShipmentModeClassifier.Classify(
            "40HC",
            "40 High Cube",
            "40HC",
            "40-high-cube");

        Assert.AreEqual(ImportedShipmentMode.Fcl, mode);
    }

    [TestMethod]
    public void Classify_WhenContainerIs20Dv_ReturnsFcl()
    {
        var mode = ImportShipmentModeClassifier.Classify(
            "20DV",
            "20 Dry Van",
            "20DV",
            "20-dry-van");

        Assert.AreEqual(ImportedShipmentMode.Fcl, mode);
    }

    [TestMethod]
    public void Classify_WhenContainerIsAir_ReturnsAir()
    {
        var mode = ImportShipmentModeClassifier.Classify(
            "AIR",
            "AIR",
            "AIR",
            "air");

        Assert.AreEqual(ImportedShipmentMode.Air, mode);
    }

    [TestMethod]
    public void Classify_WhenSnapshotIsUnknown_UsesRawEquipmentBeforeRawTariffMode()
    {
        const string raw = """
        {
          "Raw": {
            "Equipo": "LCL",
            "TariffMode": "FCL"
          }
        }
        """;

        var mode = ImportShipmentModeClassifier.Classify(
            "Por asignar",
            "Por asignar",
            "PENDING",
            "por-asignar",
            raw);

        Assert.AreEqual(ImportedShipmentMode.Lcl, mode);
    }

    [TestMethod]
    public void Classify_WhenOnlyRawFclModeExists_ReturnsFcl()
    {
        const string raw = """
        {
          "Raw": {
            "TariffMode": "FCL"
          }
        }
        """;

        var mode = ImportShipmentModeClassifier.Classify(
            null,
            null,
            null,
            null,
            raw);

        Assert.AreEqual(ImportedShipmentMode.Fcl, mode);
    }
}

using Dhole.Pricing.Application.Imports;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ApprovedPricingTemplateDetectorTests
{
    [TestMethod]
    public void IsApprovedTemplateRawJson_WhenApprovedTemplateColumnsArePresent_ReturnsTrue()
    {
        const string rawJson = """
            {
              "Raw": {
                "Carrier": "MSK",
                "Equipo": "40HC",
                "Cantidad": "1",
                "POL": "SHANGHAI",
                "POE": "BALBOA",
                "POD": null,
                "Flete Internacional": "6600",
                "Moneda": "USD",
                "Tipo Tarifa": "SPOT",
                "ETD": "26/09/2026",
                "Commodity": "Electrónicos",
                "Válido Desde": "26/09/2026",
                "Válido Hasta": "30/09/2026",
                "Tiempo Tránsito (días)": "36",
                "Días Libres en Destino": "7",
                "Observaciones": "Sujeto a espacio"
              }
            }
            """;

        Assert.IsTrue(ApprovedPricingTemplateDetector.IsApprovedTemplateRawJson(rawJson));
    }

    [TestMethod]
    public void IsApprovedTemplateRawJson_WhenPayloadIsGeneric_ReturnsFalse()
    {
        const string rawJson = """
            {
              "Carrier": "MSC",
              "ContainerType": "40HC",
              "POL": "SHANGHAI",
              "POE": "CALDERA",
              "OceanFreight": 6900,
              "Currency": "USD",
              "RateType": "SPOT",
              "ETD": "26/09/2026",
              "ValidFrom": "26/09/2026",
              "ValidTo": "30/09/2026",
              "TransitDays": 36,
              "FreeDays": 7
            }
            """;

        Assert.IsFalse(ApprovedPricingTemplateDetector.IsApprovedTemplateRawJson(rawJson));
    }

    [TestMethod]
    public void IsApprovedTemplateRawJson_WhenJsonIsMalformed_ReturnsFalse()
    {
        Assert.IsFalse(ApprovedPricingTemplateDetector.IsApprovedTemplateRawJson("{not-json"));
    }
}

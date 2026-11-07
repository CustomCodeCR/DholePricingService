using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class CarrierDetailsTests
{
    [TestMethod]
    public void CarrierDetails_AreTrimmedAndCanBeCleared()
    {
        var rate = (RateHeader)Activator.CreateInstance(typeof(RateHeader), nonPublic: true)!;
        rate.ConfigureCarrierDetails("  Shanghái → Busan  ", "  Transbordo sujeto a espacio  ");
        Assert.AreEqual("Shanghái → Busan", rate.CarrierRoute);
        Assert.AreEqual("Transbordo sujeto a espacio", rate.CarrierComments);
        rate.ConfigureCarrierDetails(string.Empty, string.Empty);
        Assert.IsNull(rate.CarrierRoute);
        Assert.IsNull(rate.CarrierComments);
    }

    [TestMethod]
    public void LegacyNullFields_DoNotEraseSavedCarrierDetails()
    {
        var rate = (RateHeader)Activator.CreateInstance(typeof(RateHeader), nonPublic: true)!;
        rate.ConfigureCarrierDetails("Taichung vía Hong Kong", "Conexión estimada");
        rate.ConfigureCarrierDetails(null, null);
        Assert.AreEqual("Taichung vía Hong Kong", rate.CarrierRoute);
        Assert.AreEqual("Conexión estimada", rate.CarrierComments);
    }
}

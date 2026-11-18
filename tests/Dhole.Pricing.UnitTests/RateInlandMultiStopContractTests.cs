using System.Text.Json;
using Dhole.Pricing.Contracts.Rates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateInlandMultiStopContractTests
{
    [TestMethod]
    public void ExistingSinglePointPayloadRemainsDeserializable()
    {
        const string json = """{"address":"Cartago, Costa Rica","latitude":9.85,"longitude":-83.91}""";
        var legacy = JsonSerializer.Deserialize<RateInlandServiceLocation>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(legacy);
        Assert.AreEqual("Cartago, Costa Rica", legacy.Address);
        Assert.IsNull(legacy.CargoCondition);
        Assert.IsNull(legacy.BillingParty);
    }

    [TestMethod]
    public void MultiStopPayloadKeepsSeparateCargoAndCommercialResponsibility()
    {
        var points = new[]
        {
            new RateInlandServiceLocation("Punto A", null, null, "FiscalCargo", "Exporter"),
            new RateInlandServiceLocation("Punto B", 9.93m, -84.08m, "NationalizedCargo", "Importer"),
        };
        var recovered = JsonSerializer.Deserialize<RateInlandServiceLocation[]>(
            JsonSerializer.Serialize(points));
        Assert.IsNotNull(recovered);
        Assert.HasCount(2, recovered);
        Assert.AreEqual("FiscalCargo", recovered[0].CargoCondition);
        Assert.AreEqual("Exporter", recovered[0].BillingParty);
        Assert.AreEqual("NationalizedCargo", recovered[1].CargoCondition);
        Assert.AreEqual("Importer", recovered[1].BillingParty);
        Assert.IsNull(recovered[0].Latitude);
    }
}

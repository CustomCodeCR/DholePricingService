using Dhole.Pricing.Application.Features.Rates.SetRateStatus;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class OwnConsolidationReferenceTests
{
    [TestMethod]
    public void RepeatedExplicitMarkersResolveOneIdentity()
    {
        var id = Guid.NewGuid();
        var link = OwnConsolidationReference.Read([$"LCL PROPIO · ConsolidadoId: {id:D} · Consolidado: #60", $"ConsolidadoId: {id:D}"]);
        Assert.AreEqual<Guid?>(id, link.Id);
        Assert.AreEqual<int?>(60, link.Number);
    }

    [TestMethod]
    public void LegacyNumberDoesNotMatchItsPrefixOrAnAmbiguousDeparture()
    {
        var link = OwnConsolidationReference.Read(["Consolidado: #60"]);
        Assert.IsNull(link.Id);
        Assert.AreEqual<int?>(60, link.Number);
        Assert.IsNull(OwnConsolidationReference.Read(["Consolidado: #6", "Consolidado: #60"]).Number);
        Assert.IsNull(OwnConsolidationReference.Read([$"ConsolidadoId: {Guid.NewGuid():D}", $"ConsolidadoId: {Guid.NewGuid():D}"]).Id);
    }

    [TestMethod]
    public void RouteOrColoaderDoesNotInventAConsolidation()
    {
        var link = OwnConsolidationReference.Read(["Shanghai → Balboa", "Fuente LCL: Coloader", null]);
        Assert.IsNull(link.Id);
        Assert.IsNull(link.Number);
    }
}

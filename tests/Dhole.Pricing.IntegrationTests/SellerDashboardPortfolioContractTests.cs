using System.Reflection;
using Dhole.Pricing.Api.Endpoints;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class SellerDashboardPortfolioContractTests
{
    [TestMethod]
    public void SupervisionUsesExplicitSellerIdentityWithSameProjectionAsPersonalEndpoint()
    {
        var endpointType = typeof(SellerRateEndpoints);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;

        var personal = endpointType.GetMethod("GetMineAsync", flags);
        var supervisor = endpointType.GetMethod("GetAdminDashboardAsync", flags);
        var common = endpointType.GetMethod("GetForSellerAsync", flags);

        Assert.IsNotNull(personal, "La ruta personal debe permanecer disponible.");
        Assert.IsNotNull(supervisor, "La ruta de supervisión debe permanecer disponible.");
        Assert.IsNotNull(common, "Ambas rutas deben reutilizar la misma proyección publicada.");
        var sellerParameter = supervisor.GetParameters()
            .SingleOrDefault(parameter => parameter.Name == "sellerUserId");
        Assert.IsNotNull(sellerParameter,
            "Supervisión requiere la identidad Auth del vendedor, no un nombre o RateRequestId.");
        Assert.AreEqual(typeof(Guid?), sellerParameter.ParameterType,
            "El vendedor es opcional: el dashboard general se conserva sin filtro.");
        Assert.AreEqual(typeof(Guid), common.GetParameters()[0].ParameterType,
            "La proyección común solo acepta el identificador inmutable del vendedor.");
    }
}

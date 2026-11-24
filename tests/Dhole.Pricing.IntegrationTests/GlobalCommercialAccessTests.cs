using System.Security.Claims;
using Dhole.Pricing.Api.Services;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class GlobalCommercialAccessTests
{
    [TestMethod]
    public void BothScopesAreRequiredForCrossSellerActions()
    {
        var visibility = new SellerVisibilityService(null!);
        ClaimsPrincipal User(string scopes) => new(new ClaimsIdentity(
            [new Claim("scope", scopes)], "test"));
        Assert.IsFalse(visibility.HasGlobalCommercialScopes(User("pricing.rate-request.view-all")));
        Assert.IsFalse(visibility.HasGlobalCommercialScopes(User("pricing.rate-request.create")));
        Assert.IsFalse(visibility.HasGlobalCommercialScopes(User("pricing.rate-request.view-selected pricing.rate-request.create")));
        Assert.IsTrue(visibility.HasGlobalCommercialScopes(User("pricing.rate-request.view-all pricing.rate-request.create")));
        Assert.IsTrue(visibility.HasGlobalCommercialScopes(User("pricing.rate-request.*")));
    }
}

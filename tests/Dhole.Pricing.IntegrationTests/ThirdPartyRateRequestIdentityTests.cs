using System.Security.Claims;
using Dhole.Pricing.Api.Endpoints;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class ThirdPartyRateRequestIdentityTests
{
    private static ClaimsPrincipal Identity(Guid seller, string target = "pricing-rate-request-create") =>
        new(new ClaimsIdentity([
            new Claim("integration_source", "dhole-api-manager"),
            new Claim("integration_target", target),
            new Claim("integration_seller_user_id", seller.ToString())
        ], "Bearer"));

    [TestMethod]
    public void Only_dedicated_service_actor_and_contract_locked_seller_are_allowed()
    {
        var seller = Guid.NewGuid();
        var principal = Identity(seller);
        Assert.IsTrue(ThirdPartyRateRequestIdentity.IsDedicatedActor(principal,
            ThirdPartyRateRequestIdentity.ServiceActorId));
        Assert.IsTrue(ThirdPartyRateRequestIdentity.AllowsSeller(principal, seller));
        Assert.IsFalse(ThirdPartyRateRequestIdentity.AllowsSeller(principal, Guid.NewGuid()));
        Assert.IsFalse(ThirdPartyRateRequestIdentity.IsDedicatedActor(principal, seller));
        Assert.IsFalse(ThirdPartyRateRequestIdentity.IsDedicatedActor(
            Identity(seller, "pricing-rate-create"), ThirdPartyRateRequestIdentity.ServiceActorId));
    }

    [TestMethod]
    public void No_route_other_than_pricing_rate_request_post_is_authorized()
    {
        var principal = Identity(Guid.NewGuid());
        Assert.IsTrue(ThirdPartyRateRequestIdentity.IsAllowedRoute(
            principal, "POST", "/api/pricing/rate-requests/"));
        Assert.IsFalse(ThirdPartyRateRequestIdentity.IsAllowedRoute(
            principal, "PUT", "/api/pricing/rate-requests/"));
        Assert.IsFalse(ThirdPartyRateRequestIdentity.IsAllowedRoute(
            principal, "GET", "/api/pricing/rate-requests/mine"));
        Assert.IsFalse(ThirdPartyRateRequestIdentity.IsAllowedRoute(
            principal, "POST", "/api/pricing/rates/"));
    }

    [TestMethod]
    public void Missing_seller_claim_fails_closed()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("integration_source", "dhole-api-manager"),
            new Claim("integration_target", "pricing-rate-request-create")
        ], "Bearer"));
        Assert.IsFalse(ThirdPartyRateRequestIdentity.AllowsSeller(
            principal, ThirdPartyRateRequestIdentity.ServiceActorId));
    }
}

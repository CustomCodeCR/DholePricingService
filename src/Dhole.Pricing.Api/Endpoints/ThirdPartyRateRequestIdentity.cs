using System.Security.Claims;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Enforces Gateway's single-purpose rate-request identity. It cannot edit,
/// read, or create other Pricing resources, and never accepts a public seller
/// override: a signed claim binds the seller to the published API contract.
/// </summary>
public static class ThirdPartyRateRequestIdentity
{
    public static readonly Guid ServiceActorId = new("7c1d5bdc-80e2-4bc9-9ec4-f65ec114ed70");

    public static bool IsApiManagerToken(ClaimsPrincipal principal) =>
        principal.HasClaim("integration_source", "dhole-api-manager");

    public static bool IsDedicatedActor(ClaimsPrincipal principal, Guid? actorId) =>
        IsApiManagerToken(principal) &&
        principal.HasClaim("integration_target", "pricing-rate-request-create") &&
        actorId == ServiceActorId;

    public static bool AllowsSeller(ClaimsPrincipal principal, Guid sellerId) =>
        Guid.TryParse(principal.FindFirst("integration_seller_user_id")?.Value, out var allowed) &&
        allowed != Guid.Empty && allowed == sellerId;

    public static bool IsAllowedRoute(ClaimsPrincipal principal, string method, string? path) =>
        principal.HasClaim("integration_target", "pricing-rate-request-create") &&
        string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(path, "/api/pricing/rate-requests", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(path, "/api/pricing/rate-requests/", StringComparison.OrdinalIgnoreCase));
}

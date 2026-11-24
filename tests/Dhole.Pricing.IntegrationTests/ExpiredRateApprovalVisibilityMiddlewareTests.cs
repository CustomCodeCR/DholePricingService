using System.Security.Claims;
using Dhole.Pricing.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class ExpiredRateApprovalVisibilityMiddlewareTests
{
    [DataTestMethod]
    [DataRow("GET")]
    [DataRow("POST")]
    public async Task SellerApprovalRouteReachesEndpointOwnershipGuard(string method)
    {
        // No database: the generic linked-request visibility query must not run.
        // The endpoint still checks the actual quote via SellerRateAccessService.
        var called = false;
        var middleware = new SellerRateVisibilityMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = SellerContext(method, $"/api/pricing/rates/{Guid.NewGuid():D}/expired-update-approval");
        await middleware.InvokeAsync(context, null!);
        Assert.IsTrue(called);
    }

    [TestMethod]
    public async Task GeneralRateListRemainsForbiddenForRestrictedSeller()
    {
        var called = false;
        var middleware = new SellerRateVisibilityMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = SellerContext("GET", "/api/pricing/rates");
        await middleware.InvokeAsync(context, null!);
        Assert.IsFalse(called);
        Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    private static DefaultHttpContext SellerContext(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Vendedor"),
             new Claim("scope", "pricing.rate-request.create")], "test"));
        context.Request.Method = method;
        context.Request.Path = path;
        return context;
    }
}

using System.Reflection;
using System.Security.Claims;
using Dhole.Pricing.Api.Endpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class LegacyQuoteSuperAdminAccessTests
{
    private static bool CanManage(params Claim[] claims)
    {
        var method = typeof(RateEndpoints).GetMethod(
            "HasLegacyQuoteSuperAdminRole", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "The Pricing API must enforce the legacy migration role gate.");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return (bool)(method.Invoke(null, new object[] { principal }) ?? false);
    }

    [DataTestMethod]
    [DataRow("SuperAdmin")]
    [DataRow("SuperAdministrador")]
    [DataRow("SuperUsuario")]
    [DataRow("SuperUser")]
    [DataRow("superadmin")]
    public void PermitsOnlyNamedPrivilegedRoles(string role)
    {
        Assert.IsTrue(CanManage(new Claim(ClaimTypes.Role, role)));
    }

    [DataTestMethod]
    [DataRow("Admin")]
    [DataRow("Administrador")]
    [DataRow("Administrator")]
    [DataRow("Pricing")]
    [DataRow("Vendedor")]
    [DataRow("SuperAdministradorExtra")]
    public void RejectsOtherRolesEvenIfOtherwiseAllowedToManageRates(string role)
    {
        Assert.IsFalse(CanManage(new Claim(ClaimTypes.Role, role),
            new Claim("scope", "pricing.rate.view pricing.rate.update")));
    }

    [TestMethod]
    public void AcceptsLegacyRoleClaimFormatsWithoutAcceptingPartialRoleNames()
    {
        Assert.IsTrue(CanManage(new Claim("role", "SuperAdmin")));
        Assert.IsTrue(CanManage(new Claim("roles", "Pricing;SuperUsuario")));
        Assert.IsFalse(CanManage(new Claim("roles", "Administrator;Pricing")));
        Assert.IsFalse(CanManage(new Claim("scope", "*")));
        Assert.IsFalse(CanManage());
    }
}

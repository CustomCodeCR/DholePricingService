using System.Security.Claims;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Expiry is evaluated by the business calendar in Costa Rica, including quotes
/// whose status worker has not yet changed to Expired.
/// Approvals are bound to the persisted revision: they cannot authorize another edit.
/// </summary>
public static class ExpiredRateUpdateApprovalPolicy
{
    private static readonly TimeZoneInfo CostaRica =
        TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");

    public static bool RequiresApproval(RateHeader rate, DateTimeOffset now)
        => RequiresApproval(rate.Status, rate.ValidTo, now);

    public static bool RequiresApproval(
        RateStatus status, DateTime validUntil, DateTimeOffset now)
    {
        var todayInCostaRica = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, CostaRica).DateTime);
        return status == RateStatus.Expired
            || DateOnly.FromDateTime(validUntil) < todayInCostaRica;
    }

    /// <summary>
    /// La aprobación por vencimiento aplica a vendedores, no a Pricing o Superadmin.
    /// Los permisos normales del endpoint PUT y las restricciones de estado se mantienen.
    /// </summary>
    public static bool CanEditWithoutApproval(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return false;

        var canUpdatePricing = principal.Claims
            .Where(claim => claim.Type.Equals("scope", StringComparison.OrdinalIgnoreCase)
                         || claim.Type.Equals("scp", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split([' ', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(scope => scope.Equals("pricing.rate.update", StringComparison.OrdinalIgnoreCase));
        if (canUpdatePricing) return true;

        return principal.Claims
            .Where(claim => claim.Type == ClaimTypes.Role
                || claim.Type.Equals("role", StringComparison.OrdinalIgnoreCase)
                || claim.Type.Equals("roles", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split([' ', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(role => role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)
                || role.Equals("SuperAdministrador", StringComparison.OrdinalIgnoreCase)
                || role.Equals("SuperUsuario", StringComparison.OrdinalIgnoreCase)
                || role.Equals("SuperUser", StringComparison.OrdinalIgnoreCase));
    }

    public static bool RequiresApprovalForUser(
        RateHeader rate, DateTimeOffset now, ClaimsPrincipal principal)
        => RequiresApprovalForUser(rate.Status, rate.ValidTo, now, principal);

    public static bool RequiresApprovalForUser(
        RateStatus status, DateTime validUntil, DateTimeOffset now, ClaimsPrincipal principal)
        => RequiresApproval(status, validUntil, now) && !CanEditWithoutApproval(principal);

    public static Task<bool> IsApprovedAsync(
        ServiceDbContext db, Guid rateId, int revision, CancellationToken ct)
        => db.Database.SqlQueryRaw<bool>(
            """
            SELECT EXISTS(
              SELECT 1 FROM pricing."ExpiredRateUpdateApprovals"
              WHERE "RateId" = {0} AND "RevisionNumber" = {1}
                AND "Status" = 'Approved'
            ) AS "Value"
            """, rateId, revision).SingleAsync(ct);
}

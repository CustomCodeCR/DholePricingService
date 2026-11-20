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
        => rate.Status == RateStatus.Expired
           || DateOnly.FromDateTime(rate.ValidTo) <
              DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, CostaRica).DateTime);

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

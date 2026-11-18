using System.Linq.Expressions;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// A delayed heartbeat fallback cannot revive a lease whose token was explicitly
/// cleared by /release. Only an intentional resume may recover that stale token.
/// This expression stays server-side in the atomic EF Core UPDATE.
/// </summary>
public static class RateRequestLeaseClaimPolicy
{
    public static Expression<Func<RateRequest, bool>> Eligible(
        Guid id, Guid actor, Guid? suppliedToken, bool recoverOwnLease, DateTime now)
        => item => item.Id == id && item.Status == RateRequestStatus.Open
            && (
                ((item.LockExpiresAtUtc == null || item.LockExpiresAtUtc <= now)
                    && (suppliedToken == null || recoverOwnLease
                        || (item.LockedByUserId == actor && item.LockToken == suppliedToken)))
                || (item.LockedByUserId == actor
                    && ((suppliedToken != null && item.LockToken == suppliedToken)
                        || recoverOwnLease))
            );

    // The heartbeat interval is 20 seconds; require 45 seconds without a
    // successful renewal before making a foreign lease recoverable.
    public static Expression<Func<RateRequest, bool>> StaleLease(Guid id, DateTime now)
    {
        // Precompute before EF translation. The database receives a timestamp
        // parameter, not an unsupported nested DateTime expression.
        var staleCutoff = now.AddSeconds(75);
        return item => item.Id == id
            && item.Status == RateRequestStatus.Open
            && item.LockedByUserId != null
            && item.LockToken != null
            && item.LockExpiresAtUtc > now
            && item.LockExpiresAtUtc <= staleCutoff;
    }

    // Never unlock a different operator's request, nor the active request.
    public static Expression<Func<RateRequest, bool>> OtherOwnedLeases(Guid actor, Guid activeId)
        => item => item.Id != activeId
            && item.Status == RateRequestStatus.Open
            && item.LockedByUserId == actor
            && item.LockToken != null;
}

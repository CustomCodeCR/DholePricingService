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

    // Never unlock a different operator's request, nor the active request.
    public static Expression<Func<RateRequest, bool>> OtherOwnedLeases(Guid actor, Guid activeId)
        => item => item.Id != activeId
            && item.Status == RateRequestStatus.Open
            && item.LockedByUserId == actor
            && item.LockToken != null;
}

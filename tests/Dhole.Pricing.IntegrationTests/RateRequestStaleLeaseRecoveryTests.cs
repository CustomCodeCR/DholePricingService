using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestStaleLeaseRecoveryTests
{
    private static RateRequest Create() => RateRequest.Create(
        RateRequestPriority.Red, Guid.NewGuid(), "Vendedor", "seller@example.com",
        "Cliente", "Ejecutivo", "Fcl", "Shenzhen", "Caldera", "{}");

    private static void SetLease(RateRequest request, DateTime expiry)
    {
        typeof(RateRequest).GetProperty(nameof(RateRequest.LockedByUserId))!
            .SetValue(request, Guid.NewGuid());
        typeof(RateRequest).GetProperty(nameof(RateRequest.LockToken))!
            .SetValue(request, Guid.NewGuid());
        typeof(RateRequest).GetProperty(nameof(RateRequest.LockExpiresAtUtc))!
            .SetValue(request, expiry);
    }

    [TestMethod]
    public void ActiveHeartbeatNeverCanBeReleasedAsStale()
    {
        var now = DateTime.UtcNow;
        var request = Create();
        SetLease(request, now.AddSeconds(110));
        Assert.IsFalse(RateRequestLeaseClaimPolicy.StaleLease(request.Id, now)
            .Compile()(request));
    }

    [TestMethod]
    public void AbandonedLeaseCanBeReleasedOnlyAfterFortyFiveSeconds()
    {
        var now = DateTime.UtcNow;
        var request = Create();
        SetLease(request, now.AddSeconds(70));
        Assert.IsTrue(RateRequestLeaseClaimPolicy.StaleLease(request.Id, now)
            .Compile()(request));
        Assert.IsFalse(RateRequestLeaseClaimPolicy.StaleLease(Guid.NewGuid(), now)
            .Compile()(request));
    }

    [TestMethod]
    public void ExpiredOrCompletedLeasesDoNotMatchRecovery()
    {
        var now = DateTime.UtcNow;
        var request = Create();
        SetLease(request, now.AddSeconds(-1));
        Assert.IsFalse(RateRequestLeaseClaimPolicy.StaleLease(request.Id, now)
            .Compile()(request));
        SetLease(request, now.AddSeconds(50));
        request.MarkCompleted(now);
        Assert.IsFalse(RateRequestLeaseClaimPolicy.StaleLease(request.Id, now)
            .Compile()(request));
    }
}

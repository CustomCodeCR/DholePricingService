using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestLeaseClaimPolicyTests
{
    private static RateRequest NewRequest() =>
        RateRequest.Create(
            RateRequestPriority.Red, Guid.NewGuid(), "Vendedor", "vendedor@example.com",
            "Cliente", "Ejecutivo", "Fcl", "Shanghai", "Caldera", "{}");

    [TestMethod]
    public void ReleasedLeaseRejectsStaleHeartbeatClaimButAllowsIntentionalResume()
    {
        var request = NewRequest();
        var actor = Guid.NewGuid();
        var staleToken = Guid.NewGuid();
        var now = DateTime.UtcNow;
        Assert.IsFalse(RateRequestLeaseClaimPolicy.Eligible(request.Id, actor, staleToken, false, now)
            .Compile()(request));
        Assert.IsTrue(RateRequestLeaseClaimPolicy.Eligible(request.Id, actor, null, false, now)
            .Compile()(request));
        Assert.IsTrue(RateRequestLeaseClaimPolicy.Eligible(request.Id, actor, staleToken, true, now)
            .Compile()(request));
    }

    [TestMethod]
    public void ActiveLeaseCannotBeStolenByAnotherUser()
    {
        var request = NewRequest();
        var actor = Guid.NewGuid();
        var token = Guid.NewGuid();
        typeof(RateRequest).GetProperty(nameof(RateRequest.LockedByUserId))!
            .SetValue(request, actor);
        typeof(RateRequest).GetProperty(nameof(RateRequest.LockToken))!
            .SetValue(request, token);
        typeof(RateRequest).GetProperty(nameof(RateRequest.LockExpiresAtUtc))!
            .SetValue(request, DateTime.UtcNow.AddMinutes(2));
        var now = DateTime.UtcNow;
        Assert.IsFalse(RateRequestLeaseClaimPolicy.Eligible(request.Id, Guid.NewGuid(), null, true, now)
            .Compile()(request));
        Assert.IsTrue(RateRequestLeaseClaimPolicy.Eligible(request.Id, actor, token, false, now)
            .Compile()(request));
    }
}

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
    public void RevokingAbandonedLeasesIsOwnerScopedAndPreservesActiveRequest()
    {
        var actor = Guid.NewGuid();
        var active = NewRequest();
        var abandoned = NewRequest();
        var foreign = NewRequest();
        var completed = NewRequest();
        static void Own(RateRequest item, Guid user)
        {
            typeof(RateRequest).GetProperty(nameof(RateRequest.LockedByUserId))!.SetValue(item, user);
            typeof(RateRequest).GetProperty(nameof(RateRequest.LockToken))!.SetValue(item, Guid.NewGuid());
        }
        Own(active, actor);
        Own(abandoned, actor);
        Own(foreign, Guid.NewGuid());
        Own(completed, actor);
        completed.MarkCompleted(DateTime.UtcNow);
        var predicate = RateRequestLeaseClaimPolicy.OtherOwnedLeases(actor, active.Id).Compile();
        Assert.IsFalse(predicate(active));
        Assert.IsTrue(predicate(abandoned));
        Assert.IsFalse(predicate(foreign));
        Assert.IsFalse(predicate(completed));
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

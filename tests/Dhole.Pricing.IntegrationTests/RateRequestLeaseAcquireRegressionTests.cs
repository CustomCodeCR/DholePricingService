using System.Linq.Expressions;
using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestLeaseAcquireRegressionTests
{
    private static RateRequest NewRequest() =>
        RateRequest.Create(RateRequestPriority.Red, Guid.NewGuid(), "Vendedor",
            "sales@example.com", "Cliente", "Ejecutivo", "Fcl", "Shanghai", "Caldera", "{}");

    [TestMethod]
    public void ClaimPolicyAllowsUnreservedOpenRequestAndRejectsForeignLease()
    {
        var request = NewRequest();
        var actor = Guid.NewGuid();
        var now = DateTime.UtcNow;
        Assert.IsTrue(RateRequestLeaseClaimPolicy.Eligible(request.Id, actor, null, false, now)
            .Compile()(request));
        typeof(RateRequest).GetProperty(nameof(RateRequest.LockedByUserId))!
            .SetValue(request, Guid.NewGuid());
        typeof(RateRequest).GetProperty(nameof(RateRequest.LockToken))!
            .SetValue(request, Guid.NewGuid());
        typeof(RateRequest).GetProperty(nameof(RateRequest.LockExpiresAtUtc))!
            .SetValue(request, now.AddMinutes(2));
        Assert.IsFalse(RateRequestLeaseClaimPolicy.Eligible(request.Id, actor, null, true, now)
            .Compile()(request));
    }

    [TestMethod]
    public void StaleLeaseThresholdIsPrecomputedParameterForDatabaseTranslation()
    {
        var now = DateTime.UtcNow;
        var expression = RateRequestLeaseClaimPolicy.StaleLease(Guid.NewGuid(), now);
        var invocations = new List<MethodCallExpression>();
        new CallVisitor(invocations).Visit(expression);
        Assert.IsFalse(invocations.Any(call => call.Method.Name is "AddMinutes" or "AddSeconds"));
    }

    private sealed class CallVisitor(List<MethodCallExpression> calls) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            calls.Add(node);
            return base.VisitMethodCall(node);
        }
    }
}

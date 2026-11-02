using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateUpdateWindowPolicyTests
{
    [DataTestMethod]
    [DataRow(RateStatus.Open)]
    [DataRow(RateStatus.PendingApproval)]
    [DataRow(RateStatus.ApprovedByManagement)]
    [DataRow(RateStatus.Sent)]
    [DataRow(RateStatus.AcceptedByClient)]
    [DataRow(RateStatus.RejectedByClient)]
    [DataRow(RateStatus.Expired)]
    public void StandaloneOrCompletedRequestQuote_CanBeRevised(RateStatus status)
    {
        Assert.IsTrue(RateUpdateWindowPolicy.Evaluate(status, false, false).CanUpdate);
        Assert.IsTrue(RateUpdateWindowPolicy.Evaluate(status, true, false).CanUpdate);
    }

    [TestMethod]
    public void ClosedQuotation_RemainsImmutable()
    {
        Assert.IsFalse(RateUpdateWindowPolicy.Evaluate(RateStatus.Closed, false, false).CanUpdate);
        Assert.IsFalse(RateUpdateWindowPolicy.Evaluate(RateStatus.Closed, true, true).CanUpdate);
    }

    [TestMethod]
    public void OpenSellerWorkItem_StillCanBeUpdated()
    {
        var policy = RateUpdateWindowPolicy.Evaluate(RateStatus.Open, true, true);
        Assert.IsTrue(policy.CanUpdate);
        Assert.AreEqual("LinkedRequestOpen", policy.UpdateWindow);
    }
}

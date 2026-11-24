using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class SellerRateDecisionPolicyTests
{
    [TestMethod]
    [DataRow(RateStatus.Sent)]
    [DataRow(RateStatus.RequestedByClient)]
    [DataRow(RateStatus.Expired)]
    public void PublishedStatus_AllowsClientAcceptanceAndRejection(RateStatus current)
    {
        Assert.IsTrue(SellerRateDecisionPolicy.CanDecideWithoutPublishedRevision(current, RateStatus.AcceptedByClient));
        Assert.IsTrue(SellerRateDecisionPolicy.CanDecideWithoutPublishedRevision(current, RateStatus.RejectedByClient));
        Assert.IsFalse(SellerRateDecisionPolicy.CanDecideWithoutPublishedRevision(current, RateStatus.Cancelled));
    }

    [TestMethod]
    public void OpenRequiresPublishedRevisionForClientDecision()
    {
        Assert.IsTrue(SellerRateDecisionPolicy.RequiresPublishedRevision(RateStatus.Open, RateStatus.AcceptedByClient));
        Assert.IsTrue(SellerRateDecisionPolicy.RequiresPublishedRevision(RateStatus.Open, RateStatus.RejectedByClient));
        Assert.IsFalse(SellerRateDecisionPolicy.CanDecideWithoutPublishedRevision(RateStatus.Open, RateStatus.AcceptedByClient));
        Assert.IsFalse(SellerRateDecisionPolicy.RequiresPublishedRevision(RateStatus.Open, RateStatus.Cancelled));
    }

    [TestMethod]
    [DataRow(RateStatus.PendingApproval)]
    [DataRow(RateStatus.ApprovedByManagement)]
    [DataRow(RateStatus.RejectedByManagement)]
    [DataRow(RateStatus.Closed)]
    [DataRow(RateStatus.Cancelled)]
    [DataRow(RateStatus.RejectedByClient)]
    [DataRow(RateStatus.QUOVinculada)]
    public void OtherStatesCannotBypassClientDecisionPolicy(RateStatus current)
    {
        Assert.IsFalse(SellerRateDecisionPolicy.RequiresPublishedRevision(current, RateStatus.AcceptedByClient));
        Assert.IsFalse(SellerRateDecisionPolicy.CanDecideWithoutPublishedRevision(current, RateStatus.AcceptedByClient));
        Assert.IsFalse(SellerRateDecisionPolicy.CanDecideWithoutPublishedRevision(current, RateStatus.Cancelled));
    }

    [TestMethod]
    public void CancellationIsSeparateFromAcceptance()
    {
        Assert.IsTrue(SellerRateDecisionPolicy.CanDecideWithoutPublishedRevision(RateStatus.AcceptedByClient, RateStatus.Cancelled));
        Assert.IsFalse(SellerRateDecisionPolicy.CanDecideWithoutPublishedRevision(RateStatus.AcceptedByClient, RateStatus.AcceptedByClient));
    }
}

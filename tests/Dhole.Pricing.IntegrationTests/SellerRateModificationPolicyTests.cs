using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class SellerRateModificationPolicyTests
{
    [TestMethod]
    public void AcceptedQuotation_AlwaysRequiresPricing_EvenIfSellerReceivedIt()
    {
        Assert.IsTrue(SellerRateModificationPolicy.RequiresPricing(RateStatus.AcceptedByClient, false));
        Assert.IsFalse(SellerRateModificationPolicy.CanSellerRequest(
            RateStatus.AcceptedByClient, sellerReceived: true, previouslyAccepted: true, isDeleted: false));
        Assert.IsFalse(SellerRateModificationPolicy.CanSellerRequest(
            RateStatus.AcceptedByClient, sellerReceived: true, previouslyAccepted: false, isDeleted: false));
    }

    [TestMethod]
    public void PricingRevision_DoesNotReopenSellerRequestsAfterAcceptance()
    {
        Assert.IsTrue(SellerRateModificationPolicy.RequiresPricing(RateStatus.Sent, previouslyAccepted: true));
        Assert.IsFalse(SellerRateModificationPolicy.CanSellerRequest(
            RateStatus.Open, sellerReceived: true, previouslyAccepted: true, isDeleted: false));
        Assert.IsFalse(SellerRateModificationPolicy.CanSellerRequest(
            RateStatus.Sent, sellerReceived: true, previouslyAccepted: true, isDeleted: false));
    }

    [TestMethod]
    public void UnacceptedCommercialStatuses_RetainExistingSellerFlow()
    {
        foreach (var status in new[] { RateStatus.Sent, RateStatus.RequestedByClient,
            RateStatus.RejectedByClient, RateStatus.Expired })
            Assert.IsTrue(SellerRateModificationPolicy.CanSellerRequest(
                status, sellerReceived: false, previouslyAccepted: false, isDeleted: false));

        Assert.IsTrue(SellerRateModificationPolicy.CanSellerRequest(
            RateStatus.Open, sellerReceived: true, previouslyAccepted: false, isDeleted: false));
        Assert.IsFalse(SellerRateModificationPolicy.CanSellerRequest(
            RateStatus.Open, sellerReceived: false, previouslyAccepted: false, isDeleted: false));
        Assert.IsFalse(SellerRateModificationPolicy.CanSellerRequest(
            RateStatus.Closed, sellerReceived: true, previouslyAccepted: false, isDeleted: false));
        Assert.IsFalse(SellerRateModificationPolicy.CanSellerRequest(
            RateStatus.Cancelled, sellerReceived: true, previouslyAccepted: false, isDeleted: false));
        Assert.IsFalse(SellerRateModificationPolicy.CanSellerRequest(
            RateStatus.Sent, sellerReceived: true, previouslyAccepted: false, isDeleted: true));
    }
}

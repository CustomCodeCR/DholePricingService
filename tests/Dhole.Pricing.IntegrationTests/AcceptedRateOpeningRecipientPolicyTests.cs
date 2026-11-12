using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Rates.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class AcceptedRateOpeningRecipientPolicyTests
{
    [TestMethod]
    public void PricingAcceptance_CopiesPricingAndAssignedSeller()
    {
        var recipients = AcceptedRateOpeningsNotificationService.CopyRecipientsFor(
            RateAcceptanceActor.Pricing,
            ["seller@example.com", "SELLER@example.com", "aperturatramites@grupocastrofallas.com"]);

        CollectionAssert.AreEquivalent(
            new[] { "pricingcf@castrofallas.com", "seller@example.com" }, recipients);
    }

    [TestMethod]
    public void SellerAcceptance_KeepsPricingAndSellerCopies()
    {
        var recipients = AcceptedRateOpeningsNotificationService.CopyRecipientsFor(
            RateAcceptanceActor.Seller, ["seller@example.com"]);

        CollectionAssert.AreEquivalent(
            new[] { "pricingcf@castrofallas.com", "seller@example.com" }, recipients);
    }

    [TestMethod]
    public void PendingIdtra_IsNotAReasonToSuppressAcceptanceEmail()
    {
        Assert.IsTrue(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.AcceptedByClient, null));
        Assert.IsFalse(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.Sent, null));
    }
}

using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Rates.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class AcceptedRateIdtraNotificationPolicyTests
{
    [TestMethod]
    public void SellerAcceptanceGoesToAperturaWithPricingInCopy()
    {
        Assert.AreEqual(
            "aperturatramite@grupocastrofallas.com",
            AcceptedRateOpeningsNotificationService.RecipientFor(RateAcceptanceActor.Seller));
        CollectionAssert.AreEqual(
            new[] { "pricingcf@castrofallas.com" },
            AcceptedRateOpeningsNotificationService.CopyRecipientsFor(RateAcceptanceActor.Seller));
    }

    [TestMethod]
    public void PricingAcceptanceGoesToAperturaTramiteWithoutCopy()
    {
        Assert.AreEqual(
            "aperturatramite@grupocastrofallas.com",
            AcceptedRateOpeningsNotificationService.RecipientFor(RateAcceptanceActor.Pricing));
        Assert.HasCount(0,
            AcceptedRateOpeningsNotificationService.CopyRecipientsFor(RateAcceptanceActor.Pricing));
    }

    [TestMethod]
    public void NotificationIsOnlyForAcceptedRatesWithoutIdtra()
    {
        Assert.IsTrue(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.AcceptedByClient, null));
        Assert.IsTrue(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.AcceptedByClient, " "));
        Assert.IsFalse(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.AcceptedByClient, "IDTRA-123"));
        Assert.IsFalse(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.Sent, null));
    }
}

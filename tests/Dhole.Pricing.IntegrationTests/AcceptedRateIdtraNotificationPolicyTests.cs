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
            "aperturatramites@grupocastrofallas.com",
            AcceptedRateOpeningsNotificationService.RecipientFor(RateAcceptanceActor.Seller));
        CollectionAssert.AreEqual(
            new[] { "pricingcf@castrofallas.com" },
            AcceptedRateOpeningsNotificationService.CopyRecipientsFor(RateAcceptanceActor.Seller));
    }

    [TestMethod]
    public void SellerAcceptanceCopiesOnlyTheAssignedSellerWithNoDuplicatesOrInvalidAddresses()
    {
        CollectionAssert.AreEqual(
            new[] { "pricingcf@castrofallas.com", "rchaves@castrofallas.com" },
            AcceptedRateOpeningsNotificationService.CopyRecipientsFor(
                RateAcceptanceActor.Seller,
                [
                    " RChaves@castrofallas.com ",
                    "rchaves@castrofallas.com",
                    "pricingcf@castrofallas.com",
                    "aperturatramites@grupocastrofallas.com",
                    "dirección inválida",
                ]));
    }

    [TestMethod]
    public void PricingAcceptanceCopiesPricingAndAssignedSeller()
    {
        CollectionAssert.AreEqual(
            new[] { "pricingcf@castrofallas.com", "rchaves@castrofallas.com" },
            AcceptedRateOpeningsNotificationService.CopyRecipientsFor(
                RateAcceptanceActor.Pricing,
                ["rchaves@castrofallas.com"]));
    }

    [TestMethod]
    public void PricingAcceptanceGoesToAperturaTramiteWithPricingInCopy()
    {
        Assert.AreEqual(
            "aperturatramites@grupocastrofallas.com",
            AcceptedRateOpeningsNotificationService.RecipientFor(RateAcceptanceActor.Pricing));
        CollectionAssert.AreEqual(
            new[] { "pricingcf@castrofallas.com" },
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
        // IDTRA presentado junto con la aceptación: no generar ni preparar email.
        Assert.IsFalse(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.AcceptedByClient, null, "IDTRA-123"));
        Assert.IsFalse(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.AcceptedByClient, " ", "  IDTRA-2026-009  "));
        Assert.IsTrue(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.AcceptedByClient, null, " "));
        Assert.IsFalse(AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            RateStatus.Sent, null));
    }
}

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
                    "aperturatramite@grupocastrofallas.com",
                    "dirección inválida",
                ]));
    }

    [TestMethod]
    public void PricingAcceptanceIgnoresSellerCopies()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            AcceptedRateOpeningsNotificationService.CopyRecipientsFor(
                RateAcceptanceActor.Pricing,
                ["rchaves@castrofallas.com"]));
    }

    [TestMethod]
    public void PricingAcceptanceGoesToAperturaTramiteWithoutCopy()
    {
        Assert.AreEqual(
            "aperturatramite@grupocastrofallas.com",
            AcceptedRateOpeningsNotificationService.RecipientFor(RateAcceptanceActor.Pricing));
        Assert.AreEqual(
            0,
            AcceptedRateOpeningsNotificationService.CopyRecipientsFor(RateAcceptanceActor.Pricing).Length);
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

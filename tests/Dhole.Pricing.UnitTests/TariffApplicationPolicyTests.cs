using Dhole.Pricing.Application.Features.Rates.DuplicateRate;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class TariffApplicationPolicyTests
{
    private static readonly DateTime Today = new(2026, 11, 5);

    [TestMethod]
    public void ApprovedMasterWithFutureValidity_CanBeAppliedBeforeStartDate()
    {
        // Regression: QUO-00000-000544, approved on 05 Nov with validity on 12 Nov.
        Assert.IsTrue(CanApply(
            RateStatus.ApprovedByManagement,
            new DateTime(2026, 11, 12),
            new DateTime(2026, 11, 12)));
    }

    [TestMethod]
    public void ApprovedMasterWithCurrentValidity_CanBeApplied()
    {
        Assert.IsTrue(CanApply(RateStatus.ApprovedByManagement, Today.AddDays(-2), Today));
    }

    [TestMethod]
    public void ExpiredMaster_CannotBeApplied()
    {
        Assert.IsFalse(CanApply(RateStatus.ApprovedByManagement, Today.AddDays(-10), Today.AddDays(-1)));
    }

    [TestMethod]
    public void PendingOrRejectedMaster_CannotBeApplied()
    {
        Assert.IsFalse(CanApply(RateStatus.PendingApproval, Today, Today.AddDays(7)));
        Assert.IsFalse(CanApply(RateStatus.RejectedByManagement, Today, Today.AddDays(7)));
    }

    [TestMethod]
    public void InvalidPeriod_CannotBeApplied()
    {
        Assert.IsFalse(CanApply(RateStatus.ApprovedByManagement, Today.AddDays(2), Today));
    }

    [TestMethod]
    public void NonMasterOrSpotTariff_CannotBeApplied()
    {
        Assert.IsFalse(TariffApplicationPolicy.CanApply(
            RateType.Spot, "TARIFARIO MAESTRO", RateStatus.Open,
            Today, Today.AddDays(7), Today));
        Assert.IsFalse(TariffApplicationPolicy.CanApply(
            RateType.Tariff, "CLIENTE NORMAL", RateStatus.Open,
            Today, Today.AddDays(7), Today));
    }

    private static bool CanApply(RateStatus status, DateTime validFrom, DateTime validTo) =>
        TariffApplicationPolicy.CanApply(
            RateType.Tariff, "TARIFARIO MAESTRO", status,
            validFrom, validTo, Today);
}

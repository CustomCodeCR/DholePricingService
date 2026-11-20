using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class ExpiredRateUpdateApprovalPolicyTests
{
    private static readonly DateTimeOffset Today =
        new(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(-6));

    [TestMethod]
    public void ExpiredStatus_AlwaysRequiresApproval()
    {
        Assert.IsTrue(ExpiredRateUpdateApprovalPolicy.RequiresApproval(
            RateStatus.Expired, new DateTime(2026, 11, 30), Today));
    }

    [TestMethod]
    public void SentWithYesterdayValidity_RequiresApproval_WithoutStatusWorker()
    {
        Assert.IsTrue(ExpiredRateUpdateApprovalPolicy.RequiresApproval(
            RateStatus.Sent, new DateTime(2026, 11, 18), Today));
    }

    [TestMethod]
    public void SentWithTodaysValidity_IsStillValidThroughCostaRicaBusinessDay()
    {
        Assert.IsFalse(ExpiredRateUpdateApprovalPolicy.RequiresApproval(
            RateStatus.Sent, new DateTime(2026, 11, 19), Today));
    }

    [TestMethod]
    public void SentWithTomorrowValidity_DoesNotRequireApproval()
    {
        Assert.IsFalse(ExpiredRateUpdateApprovalPolicy.RequiresApproval(
            RateStatus.Sent, new DateTime(2026, 11, 20), Today));
    }

    [TestMethod]
    public void UtcDayChange_DoesNotAdvanceCostaRicaBusinessDate()
    {
        var lateCostaRica = new DateTimeOffset(2026, 11, 19, 23, 59, 0, TimeSpan.FromHours(-6));
        Assert.IsFalse(ExpiredRateUpdateApprovalPolicy.RequiresApproval(
            RateStatus.Sent, new DateTime(2026, 11, 19), lateCostaRica));
    }
}

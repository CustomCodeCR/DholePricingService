using Dhole.Pricing.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class SellerRateExpiryReminderPolicyTests
{
    // The quotation expires at the END of 05-Nov-2026 Costa Rica time.
    private static readonly DateTime ValidTo = new(2026, 11, 5);

    [DataTestMethod]
    [DataRow("2026-10-31T15:00:00Z", "5d")]
    [DataRow("2026-10-31T23:30:00Z", "5d")]
    [DataRow("2026-11-02T15:00:00Z", "3d")]
    [DataRow("2026-11-04T17:00:00Z", "1d")]
    [DataRow("2026-11-06T00:00:00Z", "6h")]
    [DataRow("2026-11-06T05:45:00Z", "6h")]
    public void Sends_at_each_business_milestone(string utcTime, string expectedMilestone)
    {
        var now = DateTimeOffset.Parse(utcTime, System.Globalization.CultureInfo.InvariantCulture);
        var result = SellerRateExpiryReminderPolicy.Due(ValidTo, now);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedMilestone, result.Key);
        Assert.AreEqual(
            new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero),
            result.ExpiresAtUtc);
    }

    [DataTestMethod]
    [DataRow("2026-10-31T14:59:59Z")] // before 9AM local
    [DataRow("2026-11-01T16:00:00Z")] // no overdue 5-day reminder
    [DataRow("2026-11-03T16:00:00Z")] // no overdue 3-day reminder
    [DataRow("2026-11-05T17:00:00Z")] // no overdue 1-day reminder
    [DataRow("2026-11-06T06:00:00Z")] // expired
    public void Does_not_queue_outside_its_stage_or_after_expiration(string utcTime)
    {
        var now = DateTimeOffset.Parse(utcTime, System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsNull(SellerRateExpiryReminderPolicy.Due(ValidTo, now));
    }

    [DataTestMethod]
    [DataRow("Castro Fallas", true)]
    [DataRow(" castro   fallas ", true)]
    [DataRow("CASTRO FALLAS", true)]
    [DataRow("José Pablo Mesén", false)]
    [DataRow(null, true)]
    public void Suppresses_corporate_or_missing_seller(string? sellerName, bool excluded)
    {
        Assert.AreEqual(excluded, SellerRateExpiryReminderPolicy.IsExcludedSeller(sellerName));
    }
}

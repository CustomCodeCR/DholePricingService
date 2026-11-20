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

    [TestMethod]
    public void One_day_is_due_at_nine_am_Costa_Rica_and_never_the_day_after()
    {
        Assert.IsNull(SellerRateExpiryReminderPolicy.Due(
            ValidTo, DateTimeOffset.Parse("2026-11-04T14:59:59Z")));
        Assert.AreEqual("1d", SellerRateExpiryReminderPolicy.Due(
            ValidTo, DateTimeOffset.Parse("2026-11-04T15:00:00Z"))?.Key);
        Assert.IsNull(SellerRateExpiryReminderPolicy.Due(
            ValidTo, DateTimeOffset.Parse("2026-11-05T06:00:00Z")));
    }

    [TestMethod]
    public void Prefers_request_identity_only_when_request_seller_matches_executive()
    {
        var id = Guid.NewGuid();
        Assert.AreEqual(id, SellerRateExpiryReminderPolicy.ResolveSellerUserId(
            "José Pablo Mesén", id, "Jose Pablo Mesen", "seller@example.com", []));
        Assert.IsNull(SellerRateExpiryReminderPolicy.ResolveSellerUserId(
            "Otra Persona", id, "Jose Pablo Mesen", "seller@example.com", []));
    }

    [TestMethod]
    public void Resolves_assigned_seller_from_auth_unambiguously()
    {
        var id = Guid.NewGuid();
        var anotherId = Guid.NewGuid();
        var people = new[]
        {
            new SellerDirectoryUser(id, "seller@example.com", "José Pablo Mesén", "jpmesen"),
            new SellerDirectoryUser(anotherId, "other@example.com", "Otra Persona", "other"),
        };
        Assert.AreEqual(id, SellerRateExpiryReminderPolicy.ResolveSellerUserId(
            "JOSE PABLO MESEN", anotherId, "Otra Persona", "other@example.com", people));
        Assert.IsNull(SellerRateExpiryReminderPolicy.ResolveSellerUserId(
            "Castro Fallas", id, "Castro Fallas", "seller@example.com", people));
    }

    [TestMethod]
    public void Rejects_ambiguous_auth_matches()
    {
        var users = new[]
        {
            new SellerDirectoryUser(Guid.NewGuid(), "a@example.com", "Mismo Vendedor", null),
            new SellerDirectoryUser(Guid.NewGuid(), "b@example.com", "Mismo Vendedor", null),
        };
        Assert.IsNull(SellerRateExpiryReminderPolicy.ResolveSellerUserId(
            "Mismo Vendedor", null, null, null, users));
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

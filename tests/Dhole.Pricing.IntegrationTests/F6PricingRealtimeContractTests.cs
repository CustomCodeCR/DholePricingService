using System.Text.Json;
using Dhole.Pricing.Persistence.Messaging;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class F6PricingRealtimeContractTests
{
    [TestMethod]
    public void Feature_flags_fail_closed_for_wrong_release_and_missing_approval()
    {
        var flags = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Features:UniversalRealtimeEnabled"] = "true",
                ["Realtime:Outbox:Enabled"] = "true",
                ["Realtime:BatchApproved"] = "false",
                ["Realtime:ReleaseId"] = PricingRealtimeInvalidation.ReleaseId
            }).Build();
        Assert.IsFalse(PricingRealtimeInvalidation.IsEnabled(flags));
        flags["Realtime:BatchApproved"] = "true";
        Assert.IsTrue(PricingRealtimeInvalidation.IsEnabled(flags));
        flags["Realtime:ReleaseId"] = "wrong-release";
        Assert.IsFalse(PricingRealtimeInvalidation.IsEnabled(flags));
        flags["Realtime:ReleaseId"] = PricingRealtimeInvalidation.ReleaseId;
        flags["Realtime:Outbox:Enabled"] = "false";
        Assert.IsFalse(PricingRealtimeInvalidation.IsEnabled(flags));
        Assert.IsFalse(PricingRealtimeInvalidation.IsEnabled(null));
    }

    [TestMethod]
    public void Envelope_has_only_allowlisted_resource_metadata_and_versions()
    {
        var eventId = Guid.NewGuid();
        var rateId = Guid.NewGuid();
        var json = PricingRealtimeInvalidation.Envelope(
            eventId, rateId, 7, "updated", DateTimeOffset.UtcNow,
            "f6-pricing-ci-" + eventId.ToString("N"));
        using var doc = JsonDocument.Parse(json);
        var obj = doc.RootElement;
        Assert.AreEqual(eventId, obj.GetProperty("eventId").GetGuid());
        Assert.AreEqual("pricing.rate.updated.v1", obj.GetProperty("eventType").GetString());
        Assert.AreEqual("rate", obj.GetProperty("aggregateType").GetString());
        Assert.AreEqual(rateId, obj.GetProperty("aggregateId").GetGuid());
        Assert.AreEqual(7L, obj.GetProperty("aggregateVersion").GetInt64());
        Assert.AreEqual($"pricing:rate:{rateId:D}", obj.GetProperty("audienceRef").GetString());
        CollectionAssert.AreEquivalent(new[] { "changedFields" },
            obj.GetProperty("payload").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.IsFalse(json.Contains("clientName", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("accessToken", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("customer", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Only_known_lifecycle_actions_allowed_and_invalid_events_are_rejected()
    {
        var id = Guid.NewGuid();
        Assert.AreEqual("pricing.rate.created.v1", PricingRealtimeInvalidation.EventType("created"));
        Assert.AreEqual("pricing.rate.deleted.v1", PricingRealtimeInvalidation.EventType("deleted"));
        Assert.ThrowsExactly<ArgumentException>(() => PricingRealtimeInvalidation.EventType("any"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            PricingRealtimeInvalidation.Envelope(id, id, 0, "updated",
                DateTimeOffset.UtcNow, "correlation"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            PricingRealtimeInvalidation.Envelope(id, id, 1, "updated",
                DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)), "correlation"));
    }
}

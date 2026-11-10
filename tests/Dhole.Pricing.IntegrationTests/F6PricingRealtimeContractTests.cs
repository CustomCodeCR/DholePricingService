using System.Text.Json;
using Dhole.Pricing.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Dhole.Pricing.Persistence.Messaging;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class F6PricingRealtimeContractTests
{
    [TestMethod]
    public void Pricing_realtime_collection_routes_are_registered_under_existing_API_groups()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        app.MapCostEndpoints();
        app.MapRateEndpoints();
        var paths = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(x => x.Endpoints).OfType<RouteEndpoint>()
            .Select(x => x.RoutePattern.RawText).ToArray();
        CollectionAssert.Contains(paths, "/api/pricing/costs/realtime-access");
        CollectionAssert.Contains(paths, "/api/pricing/rates/realtime-access");
    }

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
    public void Rate_and_cost_collection_envelopes_are_fixed_and_metadata_only()
    {
        foreach (var kind in new[] { "rate-list", "cost-list" })
        {
            var eventId = Guid.NewGuid();
            var envelope = PricingRealtimeInvalidation.CollectionEnvelope(eventId,
                kind, DateTimeOffset.UtcNow, eventId.ToString("D"));
            using var json = JsonDocument.Parse(envelope);
            var root = json.RootElement;
            Assert.AreEqual("pricing." + kind + ".changed.v1",
                root.GetProperty("eventType").GetString());
            Assert.AreEqual(kind, root.GetProperty("aggregateType").GetString());
            Assert.IsTrue(root.GetProperty("aggregateVersion").GetInt64() > 0);
            Assert.IsFalse(envelope.Contains("clientName", StringComparison.Ordinal));
            Assert.IsFalse(envelope.Contains("costValue", StringComparison.Ordinal));
        }
        Assert.ThrowsExactly<ArgumentException>(() =>
            PricingRealtimeInvalidation.CollectionEnvelope(Guid.NewGuid(), "unknown",
                DateTimeOffset.UtcNow, "test"));
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

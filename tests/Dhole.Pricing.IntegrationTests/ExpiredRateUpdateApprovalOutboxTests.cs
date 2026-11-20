using Dhole.Pricing.Api.Endpoints;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class ExpiredRateUpdateApprovalOutboxTests
{
    [TestMethod]
    public void CorrelationId_FitsPostgresOutboxColumn()
    {
        var key = ExpiredRateUpdateApprovalEndpoints.BuildCorrelationId(
            Guid.NewGuid(), Guid.NewGuid(), "System",
            "pricing.rate.expired-update.requested");

        Assert.IsTrue(key.Length <= 100, $"Outbox correlation_id length is {key.Length}.");
    }

    [TestMethod]
    public void CorrelationId_DiffersPerChannelRecipientAndEvent()
    {
        var approval = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var otherManager = Guid.NewGuid();
        var a = ExpiredRateUpdateApprovalEndpoints.BuildCorrelationId(
            approval, manager, "System", "pricing.rate.expired-update.requested");
        var b = ExpiredRateUpdateApprovalEndpoints.BuildCorrelationId(
            approval, manager, "Email", "pricing.rate.expired-update.requested");
        var c = ExpiredRateUpdateApprovalEndpoints.BuildCorrelationId(
            approval, otherManager, "System", "pricing.rate.expired-update.requested");
        var d = ExpiredRateUpdateApprovalEndpoints.BuildCorrelationId(
            approval, manager, "System", "pricing.rate.expired-update.decided");
        Assert.AreNotEqual(a, b);
        Assert.AreNotEqual(a, c);
        Assert.AreNotEqual(a, d);
    }
}

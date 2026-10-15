using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateRequestSlaTests
{
    [TestMethod]
    public void Create_UsesResponseDeadlineForEachPriority()
    {
        AssertDeadline(RateRequestPriority.Red, 24);
        AssertDeadline(RateRequestPriority.Yellow, 48);
        AssertDeadline(RateRequestPriority.Green, 72);
    }

    private static void AssertDeadline(RateRequestPriority priority, int expectedHours)
    {
        var request = RateRequest.Create(
            priority,
            sellerUserId: Guid.NewGuid(),
            sellerName: "Vendedor",
            sellerEmail: null,
            clientName: "Cliente",
            executiveName: null,
            shipmentMode: "Fcl",
            originName: null,
            destinationName: null,
            payloadJson: "{}"
        );

        Assert.IsTrue(
            Math.Abs((request.DueAtUtc - request.RequestedAtUtc).TotalHours - expectedHours) < 0.001,
            $"Priority {priority} should expire after {expectedHours} hours."
        );
        Assert.AreEqual(DateTimeKind.Utc, request.RequestedAtUtc.Kind);
        Assert.AreEqual(DateTimeKind.Utc, request.DueAtUtc.Kind);
    }
}

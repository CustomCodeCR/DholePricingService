using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateRequestSlaTests
{
    [DataTestMethod]
    [DataRow(RateRequestPriority.Red, 24)]
    [DataRow(RateRequestPriority.Yellow, 48)]
    [DataRow(RateRequestPriority.Green, 72)]
    public void Create_UsesResponseDeadlineForPriority(RateRequestPriority priority, int expectedHours)
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

        Assert.AreEqual(expectedHours, (request.DueAtUtc - request.RequestedAtUtc).TotalHours);
        Assert.AreEqual(DateTimeKind.Utc, request.RequestedAtUtc.Kind);
        Assert.AreEqual(DateTimeKind.Utc, request.DueAtUtc.Kind);
    }
}

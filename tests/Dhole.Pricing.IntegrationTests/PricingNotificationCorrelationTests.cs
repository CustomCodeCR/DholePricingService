using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Persistence.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class PricingNotificationCorrelationTests
{
    private static readonly Guid RateId = Guid.Parse("ef21a53f-0a89-45a2-9180-4404cc299779");
    private static readonly Guid RequestId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [DataTestMethod]
    [DataRow("opening")]
    [DataRow("quote")]
    [DataRow("notice")]
    public void ManualResend_CorrelationFitsPhysicalOutboxColumn(string notificationType)
    {
        var key = AcceptedRateResendPolicy.BuildCorrelationId(
            notificationType, RateId, int.MaxValue, RequestId);

        Assert.IsTrue(key.Length <= IntegrationEventOutboxWriter.MaximumCorrelationIdLength,
            $"Correlation key has {key.Length} characters; outbox limit is 100.");
        Assert.AreEqual(key, IntegrationEventOutboxWriter.NormalizeCorrelationId(key));
    }

    [TestMethod]
    public void PreviousCorrelationKey_OverflowIsDeterministicallyShortened()
    {
        var oldKey =
            $"pricing-manual-notification:opening:{RateId:N}:rev:1:request:{RequestId:N}";

        Assert.IsTrue(oldKey.Length > IntegrationEventOutboxWriter.MaximumCorrelationIdLength);

        var normalized = IntegrationEventOutboxWriter.NormalizeCorrelationId(oldKey)!;
        Assert.IsTrue(normalized.Length <= IntegrationEventOutboxWriter.MaximumCorrelationIdLength);
        Assert.IsTrue(normalized.StartsWith("sha256:", StringComparison.Ordinal));
        Assert.AreEqual(normalized, IntegrationEventOutboxWriter.NormalizeCorrelationId(oldKey));
    }

    [TestMethod]
    public void NormalizationPreservesBoundedKeysAndAvoidsResendCollisions()
    {
        Assert.AreEqual("stable-key", IntegrationEventOutboxWriter.NormalizeCorrelationId(" stable-key "));
        Assert.IsNull(IntegrationEventOutboxWriter.NormalizeCorrelationId(null));

        var opening = AcceptedRateResendPolicy.BuildCorrelationId("opening", RateId, 1, RequestId);
        var quote = AcceptedRateResendPolicy.BuildCorrelationId("quote", RateId, 1, RequestId);
        var anotherAttempt = AcceptedRateResendPolicy.BuildCorrelationId(
            "opening", RateId, 1, Guid.NewGuid());
        Assert.AreNotEqual(opening, quote);
        Assert.AreNotEqual(opening, anotherAttempt);

        var legacy = $"pricing-manual-notification:opening:{RateId:N}:rev:1:request:{RequestId:N}";
        var differentOperation =
            $"pricing-manual-notification:opening:{RateId:N}:rev:2:request:{RequestId:N}";
        Assert.AreNotEqual(
            IntegrationEventOutboxWriter.NormalizeCorrelationId(legacy),
            IntegrationEventOutboxWriter.NormalizeCorrelationId(differentOperation));
    }
}

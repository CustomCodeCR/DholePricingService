using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Rates.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class AcceptedRateResendPolicyTests
{
    [DataTestMethod]
    [DataRow("opening", "opening")]
    [DataRow("QUOTE", "quote")]
    [DataRow(" notice ", "notice")]
    [DataRow(null, "opening")]
    public void SupportedNotificationTypesAreNormalized(string? input, string expected)
    {
        Assert.AreEqual(expected, AcceptedRateResendPolicy.NormalizeNotificationType(input));
        Assert.IsTrue(AcceptedRateResendPolicy.IsSupportedNotificationType(input));
    }

    [DataTestMethod]
    [DataRow("customer-email")]
    [DataRow("pricing.rate.accepted.opening")]
    [DataRow("all")]
    [DataRow("")]
    public void ArbitraryNotificationsAreNeverAllowed(string input)
    {
        Assert.IsFalse(AcceptedRateResendPolicy.IsSupportedNotificationType(input));
    }

    [TestMethod]
    public void AcceptedQuoteCanResendWithoutModifyingCommercialStatus()
    {
        Assert.IsTrue(AcceptedRateResendPolicy.CanResend(RateStatus.AcceptedByClient));
        Assert.IsFalse(AcceptedRateResendPolicy.CanResend(RateStatus.Sent));
        Assert.IsFalse(AcceptedRateResendPolicy.CanResend(RateStatus.QUOVinculada));
        Assert.IsFalse(AcceptedRateResendPolicy.CanResend(RateStatus.Cancelled));
    }

    [TestMethod]
    public void QueueResultSeparatesAttemptFailureFromSuccess()
    {
        var failedPdf = new AcceptedOpeningQueueResult(false,
            "Pricing.AcceptancePdfUnavailable", "Reports no pudo generar PDF.");
        Assert.IsFalse(failedPdf.Queued);
        Assert.AreEqual("Pricing.AcceptancePdfUnavailable", failedPdf.Code);

        var queuedNotice = new AcceptedOpeningQueueResult(true,
            "Pricing.AcceptanceEmailQueued", "Aviso preliminar en Outbox.");
        Assert.IsTrue(queuedNotice.Queued);
    }
}

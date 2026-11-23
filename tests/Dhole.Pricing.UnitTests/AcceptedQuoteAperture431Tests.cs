using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class AcceptedQuoteAperture431Tests
{
    [DataTestMethod]
    [DataRow("QUO-00000-000672", "QUO-00000-000672")]
    [DataRow("QUO-00000-000672-A", "QUO-00000-000672")]
    [DataRow("QUO-00000-000672-B", "QUO-00000-000672")]
    [DataRow(" quo-00000-000672-b ", "QUO-00000-000672")]
    public void AlternativeGroupNumber_IsSharedAcrossOptions(string input, string expected)
        => Assert.AreEqual(expected, AcceptedQuoteApertureEvent.GroupNumber(input));

    [TestMethod]
    public void EventContract_HasStableName()
        => Assert.AreEqual("pricing.quote.accepted.for-opening.v1", AcceptedQuoteApertureEvent.EventName);
}

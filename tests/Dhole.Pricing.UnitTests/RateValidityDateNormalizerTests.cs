using Dhole.Pricing.Application.Features.Rates;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateValidityDateNormalizerTests
{
    [TestMethod]
    public void DateOnlyJsonWithoutOffset_BecomesUtcMidnightWithoutChangingDay()
    {
        var parsed = System.Text.Json.JsonSerializer.Deserialize<DateTime>("\"2026-11-12\"");
        Assert.AreEqual(DateTimeKind.Unspecified, parsed.Kind);
        var normalized = RateValidityDateNormalizer.AsUtcCalendarDate(parsed);
        Assert.AreEqual(DateTimeKind.Utc, normalized.Kind);
        Assert.AreEqual(new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), normalized);
    }

    [TestMethod]
    public void LocalDateTime_DoesNotShiftTheCommercialDate()
    {
        var input = new DateTime(2026, 11, 12, 23, 45, 0, DateTimeKind.Local);
        var normalized = RateValidityDateNormalizer.AsUtcCalendarDate(input);
        Assert.AreEqual(DateTimeKind.Utc, normalized.Kind);
        Assert.AreEqual(new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), normalized);
    }

    [TestMethod]
    public void AlreadyUtcDate_IsIdempotent()
    {
        var input = new DateTime(2026, 12, 30, 0, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(input, RateValidityDateNormalizer.AsUtcCalendarDate(input));
    }
}

namespace Dhole.Pricing.Application.Features.Rates;

/// <summary>
/// Quote validity is a commercial calendar date, not a geographic instant.
/// Date-only JSON deserializes without a UTC offset while Npgsql timestamptz
/// requires UTC. Keep the entered date and store its UTC midnight.
/// </summary>
public static class RateValidityDateNormalizer
{
    public static DateTime AsUtcCalendarDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}

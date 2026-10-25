namespace Dhole.Pricing.Api.Services;

/// <summary>
/// RateHeader.ValidTo is a business calendar date. The rate is valid through
/// 23:59:59 in Costa Rica on that date; never interpret the stored midnight as
/// the expiry instant or as a server-local datetime.
/// </summary>
public static class SellerRateExpiryReminderPolicy
{
    public static readonly TimeZoneInfo CostaRicaZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");

    private static readonly TimeSpan Grace = TimeSpan.FromHours(2);

    public sealed record Reminder(string Key, string Label, DateTimeOffset ExpiresAtUtc);

    public static Reminder? Due(DateTime validTo, DateTimeOffset now)
    {
        var validityDate = validTo.Date;
        var expirationUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(validityDate.AddDays(1), DateTimeKind.Unspecified),
            CostaRicaZone), TimeSpan.Zero);

        if (now.ToUniversalTime() >= expirationUtc)
            return null;

        // In order of urgency so a late process start cannot backfill every
        // missed reminder in the same run.
        var milestones = new (string Key, string Label, DateTime LocalAt)[]
        {
            ("6h", "6 horas", validityDate.AddDays(1).AddHours(-6)),
            ("1d", "1 día", validityDate.AddDays(-1).AddHours(9)),
            ("3d", "3 días", validityDate.AddDays(-3).AddHours(9)),
            ("5d", "5 días", validityDate.AddDays(-5).AddHours(9)),
        };

        foreach (var (key, label, localAt) in milestones)
        {
            var dueUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(localAt, DateTimeKind.Unspecified),
                CostaRicaZone), TimeSpan.Zero);

            if (now.ToUniversalTime() >= dueUtc && now.ToUniversalTime() < dueUtc.Add(Grace))
                return new Reminder(key, label, expirationUtc);
        }

        return null;
    }

    public static bool IsExcludedSeller(string? executiveName)
    {
        if (string.IsNullOrWhiteSpace(executiveName)) return true;
        return string.Equals(
            string.Join(" ", executiveName.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            "Castro Fallas",
            StringComparison.OrdinalIgnoreCase);
    }
}

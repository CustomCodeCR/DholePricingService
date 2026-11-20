using System.Globalization;
using System.Text;

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

            // Day reminders run from 09:00 until midnight local time. The
            // final 6-hour reminder runs until the actual expiry. This lets
            // deployments/restarts later in the day catch their milestone
            // without accidentally sending missed earlier-day reminders.
            var windowEndUtc = key == "6h"
                ? expirationUtc
                : dueUtc.AddHours(15);
            if (now.ToUniversalTime() >= dueUtc && now.ToUniversalTime() < windowEndUtc)
                return new Reminder(key, label, expirationUtc);
        }

        return null;
    }

    /// <summary>
    /// Notify the assigned executive, never the quotation creator or an
    /// unrelated request owner. Ambiguous directory matches are discarded.
    /// </summary>
    public static Guid? ResolveSellerUserId(
        string? executiveName,
        Guid? requestSellerUserId,
        string? requestSellerName,
        string? requestSellerEmail,
        IEnumerable<SellerDirectoryUser> directoryUsers)
    {
        if (IsExcludedSeller(executiveName))
            return null;

        if (requestSellerUserId is { } requestId && requestId != Guid.Empty
            && SameName(executiveName, requestSellerName))
            return requestId;

        var sameRequestSeller = SameName(executiveName, requestSellerName);
        var matchingIds = directoryUsers
            .Where(user => user.UserId != Guid.Empty
                && (SameName(executiveName, user.DisplayName)
                    || SameName(executiveName, user.UserName)
                    || (sameRequestSeller && !string.IsNullOrWhiteSpace(requestSellerEmail)
                        && string.Equals(user.Email?.Trim(), requestSellerEmail.Trim(),
                            StringComparison.OrdinalIgnoreCase))))
            .Select(user => user.UserId)
            .Distinct()
            .Take(2)
            .ToArray();

        return matchingIds.Length == 1 ? matchingIds[0] : null;
    }

    private static bool SameName(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && NormalizeName(left) == NormalizeName(right);

    private static string NormalizeName(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var letters = decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark);
        return string.Join(" ", new string(letters.ToArray())
                .Normalize(NormalizationForm.FormC)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
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

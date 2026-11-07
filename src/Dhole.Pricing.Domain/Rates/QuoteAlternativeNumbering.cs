using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dhole.Pricing.Domain.Rates;

/// <summary>
/// Pure, deterministic identity and selection rules for grouped commercial QUOs.
/// Persistence, cross-request concurrency, status transitions, and Dynamics
/// publication must be enforced transactionally by the application layer.
/// </summary>
public static partial class QuoteAlternativeNumbering
{
    [GeneratedRegex(@"^QUO-[0-9]{5}-[0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ParentQuoPattern();

    [GeneratedRegex(@"^[A-Z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SuffixPattern();

    public static string SuffixAt(int zeroBasedIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
        var value = zeroBasedIndex;
        var suffix = string.Empty;
        do
        {
            suffix = (char)('A' + value % 26) + suffix;
            value = value / 26 - 1;
        } while (value >= 0);

        return suffix;
    }

    public static int IndexOfSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix) || !SuffixPattern().IsMatch(suffix))
            throw new ArgumentException("Invalid commercial alternative suffix.", nameof(suffix));

        long index = 0;
        foreach (var character in suffix)
        {
            index = checked(index * 26 + character - 'A' + 1);
            if (index - 1 > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(suffix));
        }

        return checked((int)(index - 1));
    }

    public static string SubQuoteNumber(string parentQuoteNumber, string suffix)
    {
        if (string.IsNullOrWhiteSpace(parentQuoteNumber) || !ParentQuoPattern().IsMatch(parentQuoteNumber))
            throw new ArgumentException("Expected QUO-00000-000000.", nameof(parentQuoteNumber));
        _ = IndexOfSuffix(suffix);
        return parentQuoteNumber + "-" + suffix;
    }

    public static string Key(QuoteAlternativeCandidate candidate)
    {
        if (candidate.PoeId == Guid.Empty)
            throw new ArgumentException("A POE is required.", nameof(candidate));
        if (candidate.CarrierId == Guid.Empty)
            throw new ArgumentException("A carrier is required for this FCL quotation option.", nameof(candidate));
        // JSON-array encoding avoids ambiguous composite keys when routes contain delimiters.
        return JsonSerializer.Serialize(new[]
        {
            candidate.PoeId.ToString("D", CultureInfo.InvariantCulture),
            candidate.CarrierId.ToString("D", CultureInfo.InvariantCulture),
            candidate.HaulageMode.ToString(),
            candidate.RouteKey?.Trim() ?? string.Empty
        });
    }

    public static IReadOnlyList<QuoteAlternativeDraft> Append(
        string parentQuoteNumber,
        IReadOnlyCollection<QuoteAlternativeCandidate> candidates,
        IReadOnlyCollection<QuoteAlternativeDraft> existing)
    {
        _ = SubQuoteNumber(parentQuoteNumber, "A");
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(existing);

        var result = existing.ToList();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var nextIndex = 0;
        foreach (var option in existing)
        {
            if (!keys.Add(option.Key))
                throw new InvalidOperationException("Duplicate existing quotation option.");
            if (option.QuoteNumber != SubQuoteNumber(parentQuoteNumber, option.Suffix))
                throw new InvalidOperationException("An alternative belongs to a different parent QUO.");
            nextIndex = Math.Max(nextIndex, checked(IndexOfSuffix(option.Suffix) + 1));
        }

        foreach (var candidate in candidates)
        {
            var key = Key(candidate);
            if (!keys.Add(key))
                continue;

            var suffix = SuffixAt(nextIndex++);
            result.Add(new QuoteAlternativeDraft(
                key, suffix, SubQuoteNumber(parentQuoteNumber, suffix), candidate));
        }

        if (result.Count < 2)
            throw new InvalidOperationException("A commercial QUO group requires at least two alternatives.");

        return result;
    }

    /// <summary>
    /// Preview-only decision. The database must impose an exclusive selected
    /// alternative constraint and serialize concurrent accepts before publishing.
    /// </summary>
    public static IReadOnlyList<QuoteAlternativeDraft> Select(
        IReadOnlyCollection<QuoteAlternativeDraft> alternatives,
        string key)
    {
        ArgumentNullException.ThrowIfNull(alternatives);
        if (!alternatives.Any(option => option.Key == key))
            throw new InvalidOperationException("Unknown quotation alternative.");
        if (alternatives.Any(option => option.Accepted && option.Key != key))
            throw new InvalidOperationException("A different alternative was already accepted.");

        return alternatives.Select(option => option with { Accepted = option.Key == key }).ToArray();
    }
}

public enum QuoteAlternativeHaulage
{
    Merchant,
    Carrier
}

public sealed record QuoteAlternativeCandidate(
    Guid PoeId,
    Guid CarrierId,
    QuoteAlternativeHaulage HaulageMode,
    string? RouteKey = null);

public sealed record QuoteAlternativeDraft(
    string Key,
    string Suffix,
    string QuoteNumber,
    QuoteAlternativeCandidate Candidate,
    bool Accepted = false);

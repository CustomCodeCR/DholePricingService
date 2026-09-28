using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Services;

namespace Dhole.Pricing.Application.MarketPricing.Normalization;

internal sealed class MarketCatalogNormalizer(IPricingConfigCatalogClient catalogClient)
{
    public async Task<MarketCatalogMatch> ResolveAsync(
        Guid? id,
        string? rawValue,
        string preferredGroup,
        IReadOnlyCollection<string> acceptedGroups,
        Func<string?, string>? canonicalizer = null,
        CancellationToken cancellationToken = default
    )
    {
        if (id.HasValue && id.Value != Guid.Empty)
        {
            var item = await catalogClient.GetActiveByIdAsync(id.Value, cancellationToken);
            if (
                item is not null
                && acceptedGroups.Contains(item.CatalogGroupSlug, StringComparer.OrdinalIgnoreCase)
            )
            {
                return ToMatch(preferredGroup, rawValue, item, 1m);
            }
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Unmatched(preferredGroup, rawValue);
        }

        var items = await LoadCandidatesAsync(acceptedGroups, cancellationToken);
        if (items.Count == 0)
        {
            return Unmatched(preferredGroup, rawValue);
        }

        canonicalizer ??= static value => MarketTextNormalizer.Normalize(value);

        var target = canonicalizer(rawValue);
        if (string.IsNullOrWhiteSpace(target))
        {
            return Unmatched(preferredGroup, rawValue);
        }

        var scored = items
            .Select(item => new
            {
                Item = item,
                Score = Score(target, item, canonicalizer),
            })
            .Where(x => x.Score > 0m)
            .OrderByDescending(x => x.Score)
            .ThenBy(x =>
                x.Item.CatalogGroupSlug.Equals(preferredGroup, StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1
            )
            .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (scored.Length == 0)
        {
            return Unmatched(preferredGroup, rawValue);
        }

        var top = scored[0];
        var ambiguous =
            scored.Length > 1
            && top.Score < 1m
            && Math.Abs(top.Score - scored[1].Score) < 0.0001m;

        if (ambiguous)
        {
            return new MarketCatalogMatch(
                preferredGroup,
                rawValue?.Trim(),
                null,
                null,
                null,
                top.Score,
                false,
                true
            );
        }

        return ToMatch(preferredGroup, rawValue, top.Item, top.Score);
    }

    private async Task<IReadOnlyCollection<PricingConfigCatalogItem>> LoadCandidatesAsync(
        IReadOnlyCollection<string> groups,
        CancellationToken cancellationToken
    )
    {
        var byId = new Dictionary<Guid, PricingConfigCatalogItem>();

        foreach (var group in groups.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var rows = await catalogClient.GetActiveByGroupAsync(group, cancellationToken);
            foreach (var row in rows)
            {
                byId[row.Id] = row;
            }
        }

        return byId.Values.ToArray();
    }

    private static decimal Score(
        string target,
        PricingConfigCatalogItem item,
        Func<string?, string> canonicalizer
    )
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);

        Add(candidates, canonicalizer(item.Code));
        Add(candidates, canonicalizer(item.Slug));
        Add(candidates, canonicalizer(item.Name));
        Add(candidates, canonicalizer(item.Value));

        foreach (var alias in ReadAliases(item.MetadataJson))
        {
            Add(candidates, canonicalizer(alias));
        }

        if (candidates.Contains(target))
        {
            return 1m;
        }

        var compactTarget = MarketTextNormalizer.Compact(target);
        if (
            compactTarget.Length > 0
            && candidates.Any(candidate =>
                MarketTextNormalizer.Compact(candidate).Equals(
                    compactTarget,
                    StringComparison.Ordinal
                )
            )
        )
        {
            return 0.98m;
        }

        if (target.Length >= 4)
        {
            var partial = candidates.Any(candidate =>
            {
                if (candidate.Length < 4)
                {
                    return false;
                }

                return candidate.StartsWith(target + " ", StringComparison.Ordinal)
                    || target.StartsWith(candidate + " ", StringComparison.Ordinal)
                    || candidate.StartsWith(target + ",", StringComparison.Ordinal)
                    || target.StartsWith(candidate + ",", StringComparison.Ordinal);
            });

            if (partial)
            {
                return 0.90m;
            }
        }

        return 0m;
    }

    private static IEnumerable<string> ReadAliases(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            yield break;
        }

        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(metadataJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!IsAliasProperty(property.Name))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            yield return value;
                        }
                    }
                }
            }
        }
    }

    private static bool IsAliasProperty(string name)
    {
        var normalized = MarketTextNormalizer.Compact(name);
        return normalized is
            "ALIAS"
            or "ALIASES"
            or "SYNONYM"
            or "SYNONYMS"
            or "ALTERNATENAME"
            or "ALTERNATENAMES"
            or "IATA"
            or "UNLOCODE"
            or "SCAC"
            or "ISOCODE"
            or "KINDCODE"
            or "SIZE";
    }

    private static void Add(ISet<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    private static MarketCatalogMatch ToMatch(
        string group,
        string? rawValue,
        PricingConfigCatalogItem item,
        decimal confidence
    ) =>
        new(
            group,
            rawValue?.Trim(),
            item.Id,
            item.SnapshotName(preferValue: true),
            item.Code,
            confidence,
            true
        );

    private static MarketCatalogMatch Unmatched(string group, string? rawValue) =>
        new(group, rawValue?.Trim(), null, null, null, 0m, false);
}

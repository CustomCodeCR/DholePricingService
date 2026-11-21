namespace Dhole.Pricing.Application.Features.Rates.UpdateRate;

/// <summary>
/// A persisted RateDetail ID identifies one row, not a catalog cost name. Legacy
/// wizard snapshots can contain several legitimate charges (e.g. two HBL costs)
/// with the same stale detail ID. Keep the row matching the persisted CostId and
/// create the remaining distinct rows, without overwriting or silently discarding
/// their amounts. Exact retransmitted duplicates are idempotent.
/// </summary>
public static class RateDetailUpdateInputNormalizer
{
    /// <summary>
    /// On CFR-DDP edits, historical freight must be removed even if it is still
    /// included in a stale browser snapshot. Strip only true Freight detail rows;
    /// a destination/agent charge accidentally carrying a historical Freight ID
    /// is a NEW detail, not an update to the Freight detail being removed.
    /// Preserve all other CostIds, amounts, quantities and client billing data.
    /// </summary>
    public static IReadOnlyList<UpsertRateExtraDetailCommandItem> WithoutQuotedFreight(
        IReadOnlyCollection<UpsertRateExtraDetailCommandItem> requested,
        IReadOnlySet<Guid> persistedFreightDetailIds)
    {
        return requested
            .Where(detail => detail.CostDetailType != Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Freight)
            .Select(detail => detail.Id is Guid id && persistedFreightDetailIds.Contains(id)
                ? detail with { Id = null }
                : detail)
            .ToArray();
    }

    /// <summary>
    /// Detect missing persisted RateDetail IDs. A missing row is NOT a removal:
    /// UpdateRate rejects the incomplete snapshot unless RemovedExtraDetailIds
    /// explicitly authorizes its deletion. Never infer deletion from CostId/name.
    /// </summary>
    public static IReadOnlyCollection<Guid> MissingPersistedDetailIds(
        IReadOnlyCollection<UpsertRateExtraDetailCommandItem> normalizedDetails,
        IEnumerable<Guid> persistedDetailIds)
    {
        var retainedIds = normalizedDetails
            .Where(detail => detail.Id.HasValue)
            .Select(detail => detail.Id!.Value)
            .ToHashSet();

        return persistedDetailIds.Where(id => !retainedIds.Contains(id)).ToArray();
    }


    /// <summary>
    /// A full own-LCL snapshot that omits saved Insurance or Manejos is likely
    /// an incomplete wizard hydration. Reject before mutating the quote unless
    /// the caller explicitly asked to remove those saved detail IDs.
    /// </summary>
    public static IReadOnlyCollection<Guid> MissingOwnLclProtectedDetailIds(
        IReadOnlyCollection<UpsertRateExtraDetailCommandItem> normalizedDetails,
        IEnumerable<(Guid Id, string Name, Dhole.Pricing.Domain.Costs.Enums.CostDetailType Type, string? Notes)> persisted,
        IReadOnlySet<Guid> explicitlyRemoved)
    {
        var rows = persisted.ToArray();
        if (!rows.Any(detail => (detail.Notes ?? string.Empty).Contains("LCL PROPIO", StringComparison.OrdinalIgnoreCase)
            || (detail.Notes ?? string.Empty).Contains("ConsolidadoId:", StringComparison.OrdinalIgnoreCase)
            || (detail.Notes ?? string.Empty).Contains("Fuente LCL: Propio", StringComparison.OrdinalIgnoreCase)))
            return Array.Empty<Guid>();

        var retained = normalizedDetails.Where(detail => detail.Id.HasValue)
            .Select(detail => detail.Id!.Value).ToHashSet();
        return rows.Where(detail => !retained.Contains(detail.Id)
                && !explicitlyRemoved.Contains(detail.Id)
                && (detail.Type == Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Insurance
                    || (detail.Name ?? string.Empty).Contains("Seguro", StringComparison.OrdinalIgnoreCase)
                    || (detail.Name ?? string.Empty).Contains("Insurance", StringComparison.OrdinalIgnoreCase)
                    || (detail.Name ?? string.Empty).Trim().StartsWith("Manejos", StringComparison.OrdinalIgnoreCase)))
            .Select(detail => detail.Id)
            .ToArray();
    }

    public static IReadOnlyList<UpsertRateExtraDetailCommandItem> Normalize(
        IReadOnlyCollection<UpsertRateExtraDetailCommandItem> requested,
        IReadOnlyDictionary<Guid, Guid?> persistedCostIds)
    {
        var rows = requested.ToArray();
        var preferredIndex = new Dictionary<Guid, int>();

        foreach (var group in rows
            .Select((detail, index) => (Detail: detail, Index: index))
            .Where(row => row.Detail.Id.HasValue)
            .GroupBy(row => row.Detail.Id!.Value))
        {
            var first = group.First().Index;
            if (persistedCostIds.TryGetValue(group.Key, out var existingCostId))
            {
                first = group
                    .Where(row => row.Detail.CostId == existingCostId)
                    .Select(row => (int?)row.Index)
                    .FirstOrDefault() ?? first;
            }

            preferredIndex[group.Key] = first;
        }

        var normalized = new List<UpsertRateExtraDetailCommandItem>(rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            var detail = rows[index];
            if (detail.Id is not Guid id || preferredIndex[id] == index)
            {
                normalized.Add(detail);
                continue;
            }

            // Repeated retries should not double-bill an identical line.
            if (detail == rows[preferredIndex[id]])
                continue;

            // A second, different line must be INSERTed, not UPDATEd against
            // the same persistent ID; retain its CostId and all sale/cost data.
            normalized.Add(detail with { Id = null });
        }

        return normalized;
    }
}

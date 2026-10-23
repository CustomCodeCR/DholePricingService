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
    /// When a caller explicitly submits a complete detail snapshot, any saved row
    /// absent from the final normalized list must be deleted. Use persisted detail
    /// IDs rather than CostIds: two legitimate rows may reference the same cost.
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

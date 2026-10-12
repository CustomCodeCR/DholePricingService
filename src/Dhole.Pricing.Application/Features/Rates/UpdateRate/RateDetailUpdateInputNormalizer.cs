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

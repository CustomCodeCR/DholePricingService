using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Overlay the CURRENT quotation client on request read models without changing
/// original seller forms or historical modification comparisons. This also
/// repairs the visible labels of requests created before this synchronization
/// feature, without mutating data on GET.
/// </summary>
public static class RateRequestCurrentIdentity
{
    public static async Task ApplyAsync(
        IReadOnlyCollection<RateRequest> requests,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var ids = requests.Where(request => request.RateId.HasValue)
            .Select(request => request.RateId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return;

        var identities = await db.RateHeaders.AsNoTracking()
            .Where(rate => ids.Contains(rate.Id) && !rate.IsDeleted)
            .Select(rate => new { rate.Id, rate.ClientName, rate.ExecutiveName })
            .ToListAsync(ct);
        var map = identities.ToDictionary(
            rate => rate.Id,
            rate => (rate.ClientName, rate.ExecutiveName));
        ApplySnapshots(requests, map);
    }

    public static int ApplySnapshots(
        IReadOnlyCollection<RateRequest> requests,
        IReadOnlyDictionary<Guid, (string? ClientName, string? ExecutiveName)> rates)
    {
        var changed = 0;
        foreach (var request in requests)
        {
            if (request.RateId is not Guid rateId ||
                !rates.TryGetValue(rateId, out var identity))
                continue;
            if (request.SyncCrmClientIdentity(identity.ClientName, identity.ExecutiveName))
                changed++;
        }
        return changed;
    }
}

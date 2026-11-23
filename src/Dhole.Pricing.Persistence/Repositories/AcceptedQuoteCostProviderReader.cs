using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class AcceptedQuoteCostProviderReader(ServiceDbContext db) : IAcceptedQuoteCostProviderReader
{
    public async Task<IReadOnlyDictionary<Guid, AcceptedQuoteCostProvider>> ResolveAsync(
        IReadOnlyCollection<Guid> costIds, CancellationToken cancellationToken = default)
    {
        if (costIds.Count == 0) return new Dictionary<Guid, AcceptedQuoteCostProvider>();
        var ids = costIds.ToArray();
        var suppliers = await db.Costs.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.AgentId, x.AgentName, x.CarrierId, x.CarrierName })
            .ToListAsync(cancellationToken);
        return suppliers.ToDictionary(x => x.Id, x =>
            x.AgentId.HasValue
                ? new AcceptedQuoteCostProvider(x.AgentId, x.AgentName)
                : new AcceptedQuoteCostProvider(x.CarrierId, x.CarrierName));
    }
}

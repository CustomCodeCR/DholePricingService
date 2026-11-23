using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class AcceptedQuoteExecutiveReader(ServiceDbContext db) : IAcceptedQuoteExecutiveReader
{
    public async Task<Guid?> ResolveAsync(Guid rateId, CancellationToken cancellationToken = default)
    {
        var sellers = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == rateId && x.SellerUserId.HasValue)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => x.SellerUserId)
            .Take(1)
            .ToArrayAsync(cancellationToken);
        return sellers.FirstOrDefault();
    }
}

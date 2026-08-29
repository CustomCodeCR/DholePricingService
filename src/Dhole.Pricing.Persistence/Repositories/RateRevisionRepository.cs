using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class RateRevisionRepository(ServiceDbContext db) : IRateRevisionRepository
{
    public async Task AddAsync(RateRevision revision, CancellationToken cancellationToken = default)
        => await db.Set<RateRevision>().AddAsync(revision, cancellationToken);

    public async Task<IReadOnlyCollection<RateRevision>> GetByRateHeaderIdAsync(Guid rateHeaderId, CancellationToken cancellationToken = default)
        => await db.Set<RateRevision>().AsNoTracking().Where(x => x.RateHeaderId == rateHeaderId)
            .OrderByDescending(x => x.RevisionNumber).ToListAsync(cancellationToken);
}

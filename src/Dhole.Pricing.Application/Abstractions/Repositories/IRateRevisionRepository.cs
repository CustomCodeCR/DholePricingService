using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Abstractions.Repositories;

public interface IRateRevisionRepository
{
    Task AddAsync(RateRevision revision, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RateRevision>> GetByRateHeaderIdAsync(Guid rateHeaderId, CancellationToken cancellationToken = default);
}

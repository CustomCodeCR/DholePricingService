using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class PricingMarketDecisionRepository(ServiceDbContext dbContext)
    : IPricingMarketDecisionRepository
{
    public async Task AddAsync(
        PricingMarketDecision decision,
        IReadOnlyCollection<PricingMarketDecisionObservation> observations,
        CancellationToken cancellationToken = default
    )
    {
        await dbContext.PricingMarketDecisions.AddAsync(decision, cancellationToken);

        if (observations.Count > 0)
        {
            await dbContext.PricingMarketDecisionObservations.AddRangeAsync(
                observations,
                cancellationToken
            );
        }
    }

    public Task<PricingMarketDecision?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    ) =>
        dbContext.PricingMarketDecisions
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<PricingMarketDecision?> GetLatestByRateIdAsync(
        Guid rateId,
        CancellationToken cancellationToken = default
    ) =>
        dbContext.PricingMarketDecisions
            .Where(x => x.RateId == rateId)
            .OrderByDescending(x => x.CalculatedAtUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PricingMarketDecisionObservation>> GetObservationsAsync(
        Guid decisionId,
        CancellationToken cancellationToken = default
    ) =>
        await dbContext.PricingMarketDecisionObservations
            .AsNoTracking()
            .Where(x => x.PricingMarketDecisionId == decisionId)
            .OrderByDescending(x => x.WasIncluded)
            .ThenByDescending(x => x.FinalWeight)
            .ThenBy(x => x.CompetitorRateObservationId)
            .ToListAsync(cancellationToken);
}

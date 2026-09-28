using Dhole.Pricing.Domain.MarketPricing.Entities;

namespace Dhole.Pricing.Application.Abstractions.Repositories;

public interface IPricingMarketDecisionRepository
{
    Task AddAsync(
        PricingMarketDecision decision,
        IReadOnlyCollection<PricingMarketDecisionObservation> observations,
        CancellationToken cancellationToken = default
    );

    Task<PricingMarketDecision?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );

    Task<PricingMarketDecision?> GetLatestByRateIdAsync(
        Guid rateId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<PricingMarketDecisionObservation>> GetObservationsAsync(
        Guid decisionId,
        CancellationToken cancellationToken = default
    );
}

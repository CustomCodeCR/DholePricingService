using Dhole.Pricing.Application.MarketPricing.AutoPricing;
using Dhole.Pricing.Domain.MarketPricing.Models;

namespace Dhole.Pricing.Application.MarketPricing.Persistence;

public enum MarketPricingReviewOutcome
{
    Approved = 1,
    Rejected = 2,
}

public sealed record PersistAutoPricingDecisionRequest(
    AutoPricingProposal Proposal,
    MarketComparisonKey ComparisonKey,
    decimal MinimumMarginPercentage,
    Guid? ActorUserId = null,
    string? ActorUserName = null
);

public sealed record MarketPricingDecisionPersistenceResult(
    Guid DecisionId,
    Guid RateId,
    int PersistedObservationCount,
    string AlgorithmVersion
);

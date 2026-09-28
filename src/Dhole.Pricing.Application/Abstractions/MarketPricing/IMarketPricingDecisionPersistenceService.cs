using Dhole.Pricing.Application.MarketPricing.Persistence;

namespace Dhole.Pricing.Application.Abstractions.MarketPricing;

public interface IMarketPricingDecisionPersistenceService
{
    Task<MarketPricingDecisionPersistenceResult> PersistCalculatedAsync(
        PersistAutoPricingDecisionRequest request,
        CancellationToken cancellationToken = default
    );

    Task MarkAutoAppliedAsync(
        Guid decisionId,
        decimal finalSaleTotal,
        Guid? actorUserId = null,
        string? actorUserName = null,
        CancellationToken cancellationToken = default
    );

    Task MarkManualOverrideAsync(
        Guid decisionId,
        decimal finalSaleTotal,
        Guid actorUserId,
        string reason,
        string? actorUserName = null,
        CancellationToken cancellationToken = default
    );

    Task MarkReviewedAsync(
        Guid decisionId,
        MarketPricingReviewOutcome outcome,
        Guid actorUserId,
        string? actorUserName = null,
        CancellationToken cancellationToken = default
    );
}

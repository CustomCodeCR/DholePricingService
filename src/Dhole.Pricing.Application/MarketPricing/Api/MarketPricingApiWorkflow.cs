using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.MarketPricing.AutoPricing;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Application.MarketPricing.Persistence;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.MarketPricing.Api;

internal sealed class MarketPricingApiWorkflow(
    RateMarketPricingContextBuilder contextBuilder,
    IMarketBenchmarkService benchmarkService,
    IAutoPricingService autoPricingService,
    IMarketPricingDecisionPersistenceService decisionPersistence,
    IPricingMarketDecisionRepository decisionRepository,
    ICompetitorRateObservationRepository observationRepository,
    IRateHeaderRepository rateRepository,
    IRateHeaderCacheService rateCache
) : IMarketPricingApiWorkflow
{
    private const decimal MoneyTolerance = 0.01m;

    public async Task<MarketBenchmarkDto> CalculateBenchmarkAsync(
        MarketComparisonKey key,
        DateTime referenceDate,
        MarketBenchmarkAmountKind amountKind,
        decimal targetPercentile,
        decimal competitiveCeilingPercentile,
        CancellationToken cancellationToken
    )
    {
        var benchmark = await benchmarkService.CalculateAsync(
            new MarketBenchmarkRequest(
                key,
                referenceDate,
                amountKind,
                targetPercentile,
                competitiveCeilingPercentile
            ),
            cancellationToken
        );

        var entities = await observationRepository.GetByIdsAsync(
            benchmark.Observations.Select(x => x.CompetitorRateObservationId).ToArray(),
            cancellationToken
        );

        return MarketPricingDtoMapper.ToDto(benchmark, entities);
    }

    public async Task<MarketBenchmarkDto> CalculateRateBenchmarkAsync(
        Guid rateId,
        DateTime? referenceDate,
        Guid? containerTypeId,
        MarketBenchmarkAmountKind amountKind,
        decimal targetPercentile,
        decimal competitiveCeilingPercentile,
        CancellationToken cancellationToken
    )
    {
        var context = await contextBuilder.BuildBenchmarkContextAsync(
            rateId,
            referenceDate,
            containerTypeId,
            cancellationToken
        );

        return await CalculateBenchmarkAsync(
            context.Key,
            context.ReferenceDate,
            amountKind,
            targetPercentile,
            competitiveCeilingPercentile,
            cancellationToken
        );
    }

    public async Task<AutoPricingCalculationDto> CalculateAndPersistAsync(
        Guid rateId,
        string? profileCode,
        DateTime? referenceDate,
        decimal? minimumMarginPercentage,
        MarketBenchmarkAmountKind amountKind,
        Guid? containerTypeId,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken
    )
    {
        var context = await contextBuilder.BuildAutoPricingContextAsync(
            rateId,
            profileCode,
            referenceDate,
            minimumMarginPercentage,
            amountKind,
            containerTypeId,
            cancellationToken
        );

        var proposal = await autoPricingService.CalculateAsync(
            context.ToRequest(),
            cancellationToken
        );

        var persisted = await decisionPersistence.PersistCalculatedAsync(
            new PersistAutoPricingDecisionRequest(
                proposal,
                context.Key,
                context.MinimumMarginPercentage,
                actorUserId,
                actorUserName
            ),
            cancellationToken
        );

        var entities = await observationRepository.GetByIdsAsync(
            proposal.Benchmark.Observations
                .Select(x => x.CompetitorRateObservationId)
                .ToArray(),
            cancellationToken
        );

        return MarketPricingDtoMapper.ToDto(persisted.DecisionId, proposal, entities);
    }

    public async Task<AutoPricingDecisionDto> GetLatestDecisionAsync(
        Guid rateId,
        CancellationToken cancellationToken
    )
    {
        var decision = await decisionRepository.GetLatestByRateIdAsync(
            rateId,
            cancellationToken
        );

        if (decision is null)
            throw new MarketPricingContextException(PricingErrors.MarketPricingDecisionNotFound);

        return await MapDecisionAsync(decision, cancellationToken);
    }

    public async Task<AutoPricingApplyDto> ApplyAsync(
        Guid rateId,
        Guid decisionId,
        string? profileCode,
        DateTime? referenceDate,
        decimal? minimumMarginPercentage,
        MarketBenchmarkAmountKind amountKind,
        Guid? containerTypeId,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken
    )
    {
        var decision = await LoadDecisionForRateAsync(
            decisionId,
            rateId,
            cancellationToken
        );

        var context = await contextBuilder.BuildAutoPricingContextAsync(
            rateId,
            profileCode,
            referenceDate,
            minimumMarginPercentage,
            amountKind,
            containerTypeId,
            cancellationToken
        );

        if (!RateMarketPricingContextBuilder.MatchesDecisionKey(decision, context.Key))
            throw new MarketPricingContextException(PricingErrors.MarketPricingDecisionStale);

        var proposal = await autoPricingService.CalculateAsync(
            context.ToRequest(),
            cancellationToken
        );

        if (
            !string.Equals(
                decision.AlgorithmVersion,
                proposal.AlgorithmVersion,
                StringComparison.Ordinal
            )
            || Math.Abs(
                decision.SuggestedSaleTotal - proposal.Position.SuggestedSalePrice
            ) > MoneyTolerance
        )
        {
            throw new MarketPricingContextException(PricingErrors.MarketPricingDecisionStale);
        }

        AutoPricingApplyResult applied;
        try
        {
            applied = autoPricingService.Apply(
                context.Rate,
                proposal,
                actorUserId
            );
        }
        catch (InvalidOperationException exception)
        {
            throw new MarketPricingContextException(
                PricingErrors.MarketPricingInvalidRequest(exception.Message)
            );
        }

        await decisionPersistence.MarkAutoAppliedAsync(
            decision.Id,
            applied.AppliedSaleTotal,
            actorUserId,
            actorUserName,
            cancellationToken
        );

        await rateCache.RemoveRateHeaderCacheAsync(rateId, cancellationToken);

        return new AutoPricingApplyDto(
            decision.Id,
            rateId,
            applied.AdjustedChargeCount,
            applied.PreviousSaleTotal,
            applied.AppliedSaleTotal,
            applied.AppliedMarginPercentage,
            applied.Status.ToString()
        );
    }

    public async Task<AutoPricingOverrideDto> OverrideAsync(
        Guid rateId,
        Guid decisionId,
        string reason,
        IReadOnlyCollection<(Guid RateDetailId, decimal SaleAmount)> details,
        Guid actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new MarketPricingContextException(
                PricingErrors.MarketPricingInvalidRequest(
                    "El motivo del override manual es obligatorio."
                )
            );
        }

        if (details.Count == 0)
        {
            throw new MarketPricingContextException(
                PricingErrors.MarketPricingInvalidRequest(
                    "Debe indicar al menos un detalle de venta para el override."
                )
            );
        }

        var duplicated = details
            .GroupBy(x => x.RateDetailId)
            .FirstOrDefault(x => x.Key == Guid.Empty || x.Count() > 1);

        if (duplicated is not null)
        {
            throw new MarketPricingContextException(
                PricingErrors.MarketPricingInvalidRequest(
                    "Los detalles del override deben ser únicos y válidos."
                )
            );
        }

        await LoadDecisionForRateAsync(decisionId, rateId, cancellationToken);

        var rate = await rateRepository.GetByIdWithDetailsAsync(
            rateId,
            cancellationToken
        );

        if (rate is null || rate.IsDeleted)
            throw new MarketPricingContextException(PricingErrors.RateHeaderNotFound);

        try
        {
            foreach (var detail in details)
            {
                if (detail.SaleAmount < 0m)
                {
                    throw new InvalidOperationException(
                        "El monto de venta manual no puede ser negativo."
                    );
                }

                rate.SetRateDetailSaleAmount(
                    detail.RateDetailId,
                    detail.SaleAmount,
                    actorUserId
                );
            }

            rate.SetAmounts(actorUserId);
        }
        catch (InvalidOperationException exception)
        {
            throw new MarketPricingContextException(
                PricingErrors.MarketPricingInvalidRequest(exception.Message)
            );
        }

        await decisionPersistence.MarkManualOverrideAsync(
            decisionId,
            rate.TotalSaleUsd,
            actorUserId,
            reason,
            actorUserName,
            cancellationToken
        );

        await rateCache.RemoveRateHeaderCacheAsync(rateId, cancellationToken);

        return new AutoPricingOverrideDto(
            decisionId,
            rateId,
            rate.TotalSaleUsd,
            rate.MarginPercentage,
            reason.Trim()
        );
    }

    public async Task<AutoPricingApprovalDto> ApproveAsync(
        Guid rateId,
        Guid decisionId,
        Guid actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken
    )
    {
        var decision = await LoadDecisionForRateAsync(
            decisionId,
            rateId,
            cancellationToken
        );

        await decisionPersistence.MarkReviewedAsync(
            decisionId,
            MarketPricingReviewOutcome.Approved,
            actorUserId,
            actorUserName,
            cancellationToken
        );

        var refreshed = await decisionRepository.GetByIdAsync(
            decisionId,
            cancellationToken
        ) ?? decision;

        return new AutoPricingApprovalDto(
            decisionId,
            rateId,
            actorUserId,
            refreshed.ReviewedAtUtc ?? DateTime.UtcNow,
            MarketPricingReviewOutcome.Approved.ToString()
        );
    }

    private async Task<PricingMarketDecision> LoadDecisionForRateAsync(
        Guid decisionId,
        Guid rateId,
        CancellationToken cancellationToken
    )
    {
        var decision = await decisionRepository.GetByIdAsync(
            decisionId,
            cancellationToken
        );

        if (decision is null || decision.RateId != rateId)
            throw new MarketPricingContextException(PricingErrors.MarketPricingDecisionNotFound);

        return decision;
    }

    private async Task<AutoPricingDecisionDto> MapDecisionAsync(
        PricingMarketDecision decision,
        CancellationToken cancellationToken
    )
    {
        var rows = await decisionRepository.GetObservationsAsync(
            decision.Id,
            cancellationToken
        );

        var entities = await observationRepository.GetByIdsAsync(
            rows.Select(x => x.CompetitorRateObservationId).ToArray(),
            cancellationToken
        );

        return MarketPricingDtoMapper.ToDto(decision, rows, entities);
    }
}

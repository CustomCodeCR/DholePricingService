using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Application.MarketPricing.AutoPricing;
using Dhole.Pricing.Domain.MarketPricing.Entities;

namespace Dhole.Pricing.Application.MarketPricing.Persistence;

internal sealed class MarketPricingDecisionPersistenceService(
    IPricingMarketDecisionRepository decisions,
    IPricingAuditService audit,
    IUnitOfWork unitOfWork
) : IMarketPricingDecisionPersistenceService
{
    public async Task<MarketPricingDecisionPersistenceResult> PersistCalculatedAsync(
        PersistAutoPricingDecisionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ValidatePersistRequest(request);

        var proposal = request.Proposal;
        var benchmark = proposal.Benchmark;
        var position = proposal.Position;

        var decision = PricingMarketDecision.Create(
            proposal.RateId,
            request.ComparisonKey,
            position.Cost,
            position.OriginalSale,
            position.SuggestedSalePrice,
            position.OriginalSale,
            benchmark.Average,
            benchmark.WeightedAverage,
            benchmark.Median,
            benchmark.Percentile25,
            benchmark.Percentile40,
            benchmark.Percentile50,
            benchmark.Percentile60,
            benchmark.Percentile65,
            benchmark.Percentile75,
            benchmark.TargetMarketPrice,
            benchmark.CompetitiveCeiling,
            benchmark.CompetitorCount,
            benchmark.ObservationCount,
            benchmark.ConfidenceScore,
            proposal.AlgorithmVersion,
            wasAutoApplied: false
        );

        var observationRows = benchmark.Observations
            .Select(observation =>
                PricingMarketDecisionObservation.Create(
                    decision.Id,
                    observation.CompetitorRateObservationId,
                    observation.CompetitorCompanyId,
                    observation.OriginalAmount,
                    observation.NormalizedAmount,
                    observation.ComparabilityScore,
                    observation.RecencyWeight,
                    observation.FinalWeight,
                    observation.IsOutlier,
                    observation.WasIncluded,
                    BuildExclusionReason(observation.ExclusionReason, observation.OutlierReason)
                )
            )
            .ToArray();

        await decisions.AddAsync(decision, observationRows, cancellationToken);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.MarketBenchmarkCalculated,
                Action: PricingAuditActions.Calculated,
                EntityType: PricingAuditEntityTypes.PricingMarketDecision,
                EntityId: decision.Id,
                ActorUserId: request.ActorUserId,
                ActorUserName: request.ActorUserName,
                After: DecisionSnapshot(decision),
                Payload: new
                {
                    Input = new
                    {
                        proposal.RateId,
                        request.ComparisonKey,
                        request.MinimumMarginPercentage,
                    },
                    Output = BenchmarkSnapshot(proposal),
                    MarketObservations = ObservationAuditSnapshot(proposal),
                    Weights = proposal.Benchmark.Observations.Select(x => new
                    {
                        x.CompetitorRateObservationId,
                        x.ComparabilityScore,
                        x.RecencyWeight,
                        x.FinalWeight,
                        x.IsOutlier,
                        x.WasIncluded,
                    }).ToArray(),
                    Target = proposal.Benchmark.TargetMarketPrice,
                    CompetitiveCeiling = proposal.Benchmark.CompetitiveCeiling,
                    FinalAmount = position.OriginalSale,
                    AlgorithmVersion = proposal.AlgorithmVersion,
                }
            ),
            cancellationToken
        );

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.AutoPricingCalculated,
                Action: PricingAuditActions.Calculated,
                EntityType: PricingAuditEntityTypes.PricingMarketDecision,
                EntityId: decision.Id,
                ActorUserId: request.ActorUserId,
                ActorUserName: request.ActorUserName,
                After: DecisionSnapshot(decision),
                Payload: new
                {
                    Input = new
                    {
                        proposal.RateId,
                        request.ComparisonKey,
                        request.MinimumMarginPercentage,
                        position.Cost,
                        position.OriginalSale,
                    },
                    Output = AutoPricingSnapshot(proposal),
                    MarketObservations = ObservationAuditSnapshot(proposal),
                    Weights = proposal.Benchmark.Observations.Select(x => new
                    {
                        x.CompetitorRateObservationId,
                        x.ComparabilityScore,
                        x.RecencyWeight,
                        x.FinalWeight,
                    }).ToArray(),
                    Target = proposal.DesiredSalePrice,
                    FinalAmount = position.OriginalSale,
                    User = new
                    {
                        request.ActorUserId,
                        request.ActorUserName,
                    },
                    AlgorithmVersion = proposal.AlgorithmVersion,
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new MarketPricingDecisionPersistenceResult(
            decision.Id,
            decision.RateId,
            observationRows.Length,
            decision.AlgorithmVersion
        );
    }

    public async Task MarkAutoAppliedAsync(
        Guid decisionId,
        decimal finalSaleTotal,
        Guid? actorUserId = null,
        string? actorUserName = null,
        CancellationToken cancellationToken = default
    )
    {
        var decision = await LoadDecisionAsync(decisionId, cancellationToken);
        ValidateFinalSale(finalSaleTotal);

        var before = DecisionSnapshot(decision);
        decision.MarkAutoApplied(finalSaleTotal);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.AutoPricingApplied,
                Action: PricingAuditActions.Applied,
                EntityType: PricingAuditEntityTypes.PricingMarketDecision,
                EntityId: decision.Id,
                ActorUserId: actorUserId,
                ActorUserName: actorUserName,
                Before: before,
                After: DecisionSnapshot(decision),
                Payload: new
                {
                    decision.RateId,
                    Target = decision.TargetMarketPrice,
                    decision.CompetitiveCeiling,
                    FinalAmount = finalSaleTotal,
                    decision.AlgorithmVersion,
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkManualOverrideAsync(
        Guid decisionId,
        decimal finalSaleTotal,
        Guid actorUserId,
        string reason,
        string? actorUserName = null,
        CancellationToken cancellationToken = default
    )
    {
        if (actorUserId == Guid.Empty)
            throw new InvalidOperationException("El usuario que realiza el override es obligatorio.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("El motivo del override manual es obligatorio.");

        var decision = await LoadDecisionAsync(decisionId, cancellationToken);
        ValidateFinalSale(finalSaleTotal);

        var before = DecisionSnapshot(decision);
        decision.MarkManualOverride(finalSaleTotal);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.AutoPricingManualOverride,
                Action: PricingAuditActions.ManualOverride,
                EntityType: PricingAuditEntityTypes.PricingMarketDecision,
                EntityId: decision.Id,
                ActorUserId: actorUserId,
                ActorUserName: actorUserName,
                Before: before,
                After: DecisionSnapshot(decision),
                Payload: new
                {
                    decision.RateId,
                    PreviousFinalAmount = before.FinalSaleTotal,
                    FinalAmount = finalSaleTotal,
                    ModificationReason = reason.Trim(),
                    Target = decision.TargetMarketPrice,
                    decision.CompetitiveCeiling,
                    decision.AlgorithmVersion,
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkReviewedAsync(
        Guid decisionId,
        MarketPricingReviewOutcome outcome,
        Guid actorUserId,
        string? actorUserName = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Enum.IsDefined(outcome))
            throw new InvalidOperationException("El resultado de revisión no es válido.");

        if (actorUserId == Guid.Empty)
            throw new InvalidOperationException("El usuario revisor es obligatorio.");

        var decision = await LoadDecisionAsync(decisionId, cancellationToken);
        var before = DecisionSnapshot(decision);

        decision.MarkReviewed(actorUserId);

        var approved = outcome == MarketPricingReviewOutcome.Approved;

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: approved
                    ? PricingAuditEventTypes.AutoPricingApproved
                    : PricingAuditEventTypes.AutoPricingRejected,
                Action: approved
                    ? PricingAuditActions.Approved
                    : PricingAuditActions.Rejected,
                EntityType: PricingAuditEntityTypes.PricingMarketDecision,
                EntityId: decision.Id,
                ActorUserId: actorUserId,
                ActorUserName: actorUserName,
                Before: before,
                After: DecisionSnapshot(decision),
                Payload: new
                {
                    decision.RateId,
                    Outcome = outcome.ToString(),
                    Target = decision.TargetMarketPrice,
                    FinalAmount = decision.FinalSaleTotal,
                    decision.ConfidenceScore,
                    decision.AlgorithmVersion,
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<PricingMarketDecision> LoadDecisionAsync(
        Guid decisionId,
        CancellationToken cancellationToken
    )
    {
        if (decisionId == Guid.Empty)
            throw new InvalidOperationException("DecisionId es obligatorio.");

        return await decisions.GetByIdAsync(decisionId, cancellationToken)
            ?? throw new InvalidOperationException(
                "La decisión de auto pricing solicitada no existe."
            );
    }

    private static void ValidatePersistRequest(PersistAutoPricingDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Proposal);

        if (request.Proposal.RateId == Guid.Empty)
            throw new InvalidOperationException("La propuesta requiere RateId.");

        if (request.ComparisonKey.PolId == Guid.Empty)
            throw new InvalidOperationException("La clave de comparación requiere POL.");

        if (
            request.MinimumMarginPercentage < 0m
            || request.MinimumMarginPercentage >= 100m
        )
        {
            throw new InvalidOperationException(
                "El margen mínimo debe estar entre 0 y menos de 100."
            );
        }

        if (string.IsNullOrWhiteSpace(request.Proposal.AlgorithmVersion))
            throw new InvalidOperationException("La versión del algoritmo es obligatoria.");
    }

    private static void ValidateFinalSale(decimal finalSaleTotal)
    {
        if (finalSaleTotal < 0m)
            throw new InvalidOperationException("La venta final no puede ser negativa.");
    }

    private static string? BuildExclusionReason(
        string? exclusionReason,
        string? outlierReason
    )
    {
        var values = new List<string>();

        if (!string.IsNullOrWhiteSpace(exclusionReason))
            values.Add(exclusionReason.Trim());

        if (!string.IsNullOrWhiteSpace(outlierReason))
            values.Add($"outlier: {outlierReason.Trim()}");

        if (values.Count == 0)
            return null;

        var value = string.Join(" | ", values);
        return value.Length <= 500 ? value : value[..500];
    }

    private static object BenchmarkSnapshot(AutoPricingProposal proposal)
    {
        var benchmark = proposal.Benchmark;
        return new
        {
            benchmark.CandidateCount,
            benchmark.ObservationCount,
            benchmark.CompetitorCount,
            benchmark.OutlierCount,
            benchmark.Average,
            benchmark.WeightedAverage,
            benchmark.Median,
            benchmark.Minimum,
            benchmark.Maximum,
            benchmark.StandardDeviation,
            benchmark.Percentile25,
            benchmark.Percentile40,
            benchmark.Percentile50,
            benchmark.Percentile60,
            benchmark.Percentile65,
            benchmark.Percentile75,
            benchmark.TargetPercentile,
            benchmark.CompetitiveCeilingPercentile,
            benchmark.TargetMarketPrice,
            benchmark.CompetitiveCeiling,
            benchmark.ConfidenceScore,
            benchmark.HasSufficientMarketData,
            benchmark.CarrierFallbackUsed,
            benchmark.AlgorithmVersion,
        };
    }

    private static object AutoPricingSnapshot(AutoPricingProposal proposal) =>
        new
        {
            Status = proposal.Status.ToString(),
            ApplicationMode = proposal.ApplicationMode.ToString(),
            proposal.CanApply,
            proposal.ShouldAutoApply,
            proposal.RequiresReview,
            proposal.DesiredSalePrice,
            proposal.AppliedAdjustmentAmount,
            proposal.UnallocatedAdjustmentAmount,
            proposal.MarketDeviationPercentage,
            Position = new
            {
                proposal.Position.Cost,
                proposal.Position.OriginalSale,
                proposal.Position.MinimumSalePrice,
                proposal.Position.MarketMedian,
                proposal.Position.WeightedMarketAverage,
                proposal.Position.TargetMarketPrice,
                proposal.Position.CompetitiveCeiling,
                proposal.Position.SuggestedSalePrice,
                proposal.Position.CurrentMargin,
                proposal.Position.SuggestedMargin,
                proposal.Position.AvailableHeadroom,
                proposal.Position.ConfidenceScore,
                Status = proposal.Position.Status.ToString(),
            },
            Adjustments = proposal.Adjustments.Select(x => new
            {
                x.RateDetailId,
                x.ChargeCode,
                x.CurrencyCode,
                x.Quantity,
                x.OriginalUnitSaleAmount,
                x.SuggestedUnitSaleAmount,
                x.OriginalTotalSaleUsd,
                x.SuggestedTotalSaleUsd,
                x.AdjustmentAmountUsd,
                x.WasAdjusted,
                x.IsProtected,
                x.AdjustmentPriority,
                AdjustmentStrategy = x.AdjustmentStrategy?.ToString(),
                x.ProtectionReason,
            }).ToArray(),
            ValidationIssues = proposal.ValidationIssues.Select(x => new
            {
                x.Code,
                x.Message,
                x.IsBlocking,
            }).ToArray(),
            proposal.AlgorithmVersion,
        };

    private static object ObservationAuditSnapshot(AutoPricingProposal proposal) =>
        proposal.Benchmark.Observations.Select(x => new
        {
            x.CompetitorRateObservationId,
            x.CompetitorCompanyId,
            x.CompetitorCompanyName,
            x.OriginalAmount,
            x.NormalizedAmount,
            x.ComparabilityScore,
            x.RecencyWeight,
            x.FinalWeight,
            x.IsPrimaryCarrierMarket,
            x.IsOutlier,
            x.WasIncluded,
            x.ExclusionReason,
            x.OutlierReason,
        }).ToArray();

    private static DecisionAuditSnapshot DecisionSnapshot(PricingMarketDecision decision) =>
        new(
            decision.Id,
            decision.RateId,
            decision.CalculatedAtUtc,
            decision.ComparisonIncotermId,
            decision.ComparisonPolId,
            decision.ComparisonPoeId,
            decision.ComparisonPodId,
            decision.ComparisonContainerTypeId,
            decision.ComparisonMode.ToString(),
            decision.ComparisonCarrierId,
            decision.CostTotal,
            decision.OriginalSaleTotal,
            decision.SuggestedSaleTotal,
            decision.FinalSaleTotal,
            decision.Average,
            decision.WeightedAverage,
            decision.Median,
            decision.P25,
            decision.P40,
            decision.P50,
            decision.P60,
            decision.P65,
            decision.P75,
            decision.TargetMarketPrice,
            decision.CompetitiveCeiling,
            decision.CompetitorCount,
            decision.ObservationCount,
            decision.ConfidenceScore,
            decision.AlgorithmVersion,
            decision.WasAutoApplied,
            decision.WasManuallyModified,
            decision.ReviewedByUserId,
            decision.ReviewedAtUtc,
            decision.CreatedAtUtc
        );

    private sealed record DecisionAuditSnapshot(
        Guid Id,
        Guid RateId,
        DateTime CalculatedAtUtc,
        Guid? ComparisonIncotermId,
        Guid ComparisonPolId,
        Guid? ComparisonPoeId,
        Guid? ComparisonPodId,
        Guid? ComparisonContainerTypeId,
        string ComparisonMode,
        Guid? ComparisonCarrierId,
        decimal CostTotal,
        decimal OriginalSaleTotal,
        decimal SuggestedSaleTotal,
        decimal FinalSaleTotal,
        decimal? Average,
        decimal? WeightedAverage,
        decimal? Median,
        decimal? P25,
        decimal? P40,
        decimal? P50,
        decimal? P60,
        decimal? P65,
        decimal? P75,
        decimal? TargetMarketPrice,
        decimal? CompetitiveCeiling,
        int CompetitorCount,
        int ObservationCount,
        decimal ConfidenceScore,
        string AlgorithmVersion,
        bool WasAutoApplied,
        bool WasManuallyModified,
        Guid? ReviewedByUserId,
        DateTime? ReviewedAtUtc,
        DateTime CreatedAtUtc
    );
}

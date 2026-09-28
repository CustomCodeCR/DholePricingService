using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.MarketPricing.AutoPricing;

internal sealed class AutoPricingService(IMarketBenchmarkService benchmarkService)
    : IAutoPricingService
{
    private const string AlgorithmVersion = "market-auto-pricing-v1";
    private const decimal MoneyTolerance = 0.01m;
    private const decimal PercentageTolerance = 0.0001m;
    private const int MaxDistributionPasses = 24;

    public async Task<AutoPricingProposal> CalculateAsync(
        AutoPricingRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ValidateRequest(request);

        var issues = new List<AutoPricingValidationIssue>();
        var rules = BuildRuleLookup(request.ChargeRules, issues);
        var states = request.Charges
            .Select(charge => BuildState(charge, rules, issues))
            .ToArray();

        var benchmark = await benchmarkService.CalculateAsync(
            new MarketBenchmarkRequest(
                request.MarketKey,
                request.ReferenceDate,
                request.BenchmarkAmountKind,
                request.Profile.TargetPercentile,
                request.Profile.CompetitiveCeilingPercentile
            ),
            cancellationToken
        );

        var costTotal = RoundMoney(states.Sum(x => x.CostTotalUsd));
        var originalSaleTotal = RoundMoney(states.Sum(x => x.OriginalSaleTotalUsd));
        var minimumSalePrice = CalculateMinimumSalePrice(
            costTotal,
            request.MinimumMarginPercentage
        );

        ApplyRuleBaselines(states, issues);

        var desiredSalePrice = ResolveDesiredSalePrice(
            originalSaleTotal,
            minimumSalePrice,
            benchmark.TargetMarketPrice
        );

        DistributeToTarget(states, desiredSalePrice);
        QuantizeSuggestedSales(states);

        var suggestedSaleTotal = RoundMoney(states.Sum(x => x.SuggestedSaleTotalUsd));
        var unallocatedAdjustment = RoundMoney(desiredSalePrice - suggestedSaleTotal);
        var appliedAdjustment = RoundMoney(suggestedSaleTotal - originalSaleTotal);

        var currentMargin = CalculateMargin(costTotal, originalSaleTotal);
        var suggestedMargin = CalculateMargin(costTotal, suggestedSaleTotal);

        if (Math.Abs(unallocatedAdjustment) > MoneyTolerance)
        {
            issues.Add(
                new AutoPricingValidationIssue(
                    "target_not_fully_allocated",
                    $"Los rubros ajustables no pudieron distribuir completamente {unallocatedAdjustment:0.00} USD hacia el target.",
                    false
                )
            );
        }

        if (
            suggestedMargin + PercentageTolerance
            < request.MinimumMarginPercentage
        )
        {
            issues.Add(
                new AutoPricingValidationIssue(
                    "minimum_margin_not_reached",
                    $"La venta resultante queda en {suggestedMargin:0.####}% y no alcanza el margen mínimo de {request.MinimumMarginPercentage:0.####}%.",
                    true
                )
            );
        }

        var sampleSufficient =
            benchmark.HasSufficientMarketData
            && benchmark.CompetitorCount >= request.Profile.MinimumCompetitorCount
            && benchmark.ObservationCount >= request.Profile.MinimumObservationCount;

        if (!sampleSufficient)
        {
            issues.Add(
                new AutoPricingValidationIssue(
                    "insufficient_market_data",
                    "La muestra comparable no alcanza los mínimos configurados para auto-aplicar.",
                    false
                )
            );
        }

        var marketDeviation = CalculateMarketDeviation(
            suggestedSaleTotal,
            benchmark.TargetMarketPrice
        );

        if (
            benchmark.TargetMarketPrice.HasValue
            && marketDeviation > request.Profile.MaximumMarketDeviation
        )
        {
            issues.Add(
                new AutoPricingValidationIssue(
                    "maximum_market_deviation_exceeded",
                    $"La propuesta se desvía {marketDeviation:0.####}% del target de mercado; el máximo configurado es {request.Profile.MaximumMarketDeviation:0.####}%.",
                    false
                )
            );
        }

        var marketPositionStatus = ResolveMarketPosition(
            sampleSufficient,
            suggestedMargin,
            request.MinimumMarginPercentage,
            suggestedSaleTotal,
            benchmark
        );

        if (marketPositionStatus == MarketPositionStatus.AboveCompetitiveRange)
        {
            var overCeiling = benchmark.CompetitiveCeiling.HasValue
                ? Math.Max(0m, suggestedSaleTotal - benchmark.CompetitiveCeiling.Value)
                : 0m;

            issues.Add(
                new AutoPricingValidationIssue(
                    "above_competitive_range",
                    benchmark.CompetitiveCeiling.HasValue
                        ? $"La estructura de costos requiere {overCeiling:0.00} USD sobre el techo competitivo."
                        : "La venta sugerida queda por encima del rango competitivo.",
                    false
                )
            );
        }

        var applicationMode = ResolveApplicationMode(
            request.Profile,
            benchmark.ConfidenceScore,
            sampleSufficient
        );

        if (
            applicationMode == AutoPricingApplicationMode.AutoApply
            && (
                marketPositionStatus != MarketPositionStatus.Competitive
                || issues.Any(x => !x.IsBlocking)
            )
        )
        {
            applicationMode = AutoPricingApplicationMode.AutoApplyWithReview;
        }

        if (issues.Any(x => x.IsBlocking))
        {
            applicationMode = AutoPricingApplicationMode.SuggestOnly;
        }

        var canApply = !issues.Any(x => x.IsBlocking);
        var shouldAutoApply =
            canApply
            && applicationMode is
                AutoPricingApplicationMode.AutoApply
                or AutoPricingApplicationMode.AutoApplyWithReview;

        var requiresReview =
            applicationMode == AutoPricingApplicationMode.AutoApplyWithReview
            || marketPositionStatus is
                MarketPositionStatus.AboveCompetitiveRange
                or MarketPositionStatus.BelowMinimumMargin
            || issues.Any(x => !x.IsBlocking);

        var status = ResolveAutoPricingStatus(
            sampleSufficient,
            marketPositionStatus,
            requiresReview
        );

        var position = new PricingMarketPosition(
            Cost: costTotal,
            OriginalSale: originalSaleTotal,
            MinimumSalePrice: minimumSalePrice,
            MarketMedian: benchmark.Median,
            WeightedMarketAverage: benchmark.WeightedAverage,
            TargetMarketPrice: benchmark.TargetMarketPrice,
            CompetitiveCeiling: benchmark.CompetitiveCeiling,
            SuggestedSalePrice: suggestedSaleTotal,
            CurrentMargin: currentMargin,
            SuggestedMargin: suggestedMargin,
            AvailableHeadroom: RoundMoney(desiredSalePrice - originalSaleTotal),
            ConfidenceScore: benchmark.ConfidenceScore,
            Status: marketPositionStatus
        );

        return new AutoPricingProposal(
            request.RateId,
            position,
            benchmark,
            status,
            applicationMode,
            canApply,
            shouldAutoApply,
            requiresReview,
            desiredSalePrice,
            appliedAdjustment,
            unallocatedAdjustment,
            marketDeviation,
            $"{benchmark.AlgorithmVersion}+{AlgorithmVersion}",
            states.Select(ToAdjustment).ToArray(),
            issues
        );
    }

    public AutoPricingApplyResult Apply(
        RateHeader rate,
        AutoPricingProposal proposal,
        Guid? updatedBy = null
    )
    {
        ArgumentNullException.ThrowIfNull(rate);
        ArgumentNullException.ThrowIfNull(proposal);

        if (rate.Id != proposal.RateId)
        {
            throw new InvalidOperationException(
                "La propuesta de auto pricing no corresponde a la tarifa indicada."
            );
        }

        if (!proposal.CanApply)
        {
            throw new InvalidOperationException(
                "La propuesta contiene validaciones bloqueantes y no puede aplicarse."
            );
        }

        var previousSaleTotal = rate.TotalSaleUsd;
        var adjustedCount = 0;

        foreach (var adjustment in proposal.Adjustments.Where(x => x.WasAdjusted))
        {
            if (adjustment.IsProtected)
            {
                throw new InvalidOperationException(
                    $"El rubro {adjustment.ChargeCode} está protegido y no puede auto-ajustarse."
                );
            }

            if (!rate.RateDetails.Any(x => x.Id == adjustment.RateDetailId))
            {
                throw new InvalidOperationException(
                    $"El detalle {adjustment.RateDetailId} ya no existe en la tarifa."
                );
            }

            rate.SetRateDetailSaleAmount(
                adjustment.RateDetailId,
                adjustment.SuggestedUnitSaleAmount,
                updatedBy
            );
            adjustedCount++;
        }

        rate.SetAmounts(updatedBy);

        return new AutoPricingApplyResult(
            rate.Id,
            AutoPricingStatus.AutoApplied,
            adjustedCount,
            previousSaleTotal,
            rate.TotalSaleUsd,
            rate.MarginPercentage
        );
    }

    private static Dictionary<string, ChargePricingRule> BuildRuleLookup(
        IReadOnlyCollection<ChargePricingRule> rules,
        ICollection<AutoPricingValidationIssue> issues
    )
    {
        var result = new Dictionary<string, ChargePricingRule>(StringComparer.Ordinal);

        foreach (var rule in rules.Where(x => x.IsActive))
        {
            var key = NormalizeChargeCode(rule.ChargeCode);
            if (result.TryAdd(key, rule))
            {
                continue;
            }

            issues.Add(
                new AutoPricingValidationIssue(
                    "duplicate_charge_rule",
                    $"Existe más de una regla activa para el rubro {rule.ChargeCode}.",
                    true
                )
            );
        }

        return result;
    }

    private static ChargeState BuildState(
        AutoPricingChargeInput charge,
        IReadOnlyDictionary<string, ChargePricingRule> rules,
        ICollection<AutoPricingValidationIssue> issues
    )
    {
        var key = NormalizeChargeCode(charge.ChargeCode);
        rules.TryGetValue(key, out var rule);

        var costTotalUsd = charge.CostAmount * charge.Quantity * charge.CurrencyToUsdRate;
        var originalSaleTotalUsd =
            charge.SaleAmount * charge.Quantity * charge.CurrencyToUsdRate;

        var protectedReason = ResolveProtectionReason(charge, rule);
        var isProtected = protectedReason is not null;

        decimal minimumTotalUsd = originalSaleTotalUsd;
        decimal? maximumTotalUsd = originalSaleTotalUsd;

        if (!isProtected && rule is not null)
        {
            minimumTotalUsd =
                costTotalUsd * (1m + (rule.MinimumMarkup / 100m));
            maximumTotalUsd = null;

            if (rule.MaximumMarkup.HasValue && costTotalUsd > 0m)
            {
                maximumTotalUsd =
                    costTotalUsd * (1m + (rule.MaximumMarkup.Value / 100m));
            }

            if (rule.MaximumAdjustmentAmount.HasValue)
            {
                var lowerByAdjustment = Math.Max(
                    0m,
                    originalSaleTotalUsd - rule.MaximumAdjustmentAmount.Value
                );
                var upperByAdjustment =
                    originalSaleTotalUsd + rule.MaximumAdjustmentAmount.Value;

                minimumTotalUsd = Math.Max(minimumTotalUsd, lowerByAdjustment);
                maximumTotalUsd = maximumTotalUsd.HasValue
                    ? Math.Min(maximumTotalUsd.Value, upperByAdjustment)
                    : upperByAdjustment;
            }

            if (
                maximumTotalUsd.HasValue
                && maximumTotalUsd.Value + MoneyTolerance < minimumTotalUsd
            )
            {
                issues.Add(
                    new AutoPricingValidationIssue(
                        "invalid_charge_rule_bounds",
                        $"La regla {rule.ChargeCode} genera un máximo inferior a su mínimo.",
                        true
                    )
                );

                maximumTotalUsd = minimumTotalUsd;
            }
        }

        return new ChargeState(
            charge,
            rule,
            RoundInternal(costTotalUsd),
            RoundInternal(originalSaleTotalUsd),
            RoundInternal(originalSaleTotalUsd),
            RoundInternal(minimumTotalUsd),
            maximumTotalUsd.HasValue ? RoundInternal(maximumTotalUsd.Value) : null,
            isProtected,
            protectedReason
        );
    }

    private static void ApplyRuleBaselines(
        IReadOnlyCollection<ChargeState> states,
        ICollection<AutoPricingValidationIssue> issues
    )
    {
        foreach (var state in states)
        {
            if (state.IsProtected || state.Rule is null)
            {
                continue;
            }

            if (state.Rule.AdjustmentStrategy == ChargeAdjustmentStrategy.FixedMarkup)
            {
                var fixedTarget =
                    state.CostTotalUsd * (1m + (state.Rule.MinimumMarkup / 100m));
                var bounded = Bound(fixedTarget, state.MinimumTotalUsd, state.MaximumTotalUsd);

                if (Math.Abs(bounded - fixedTarget) > MoneyTolerance)
                {
                    issues.Add(
                        new AutoPricingValidationIssue(
                            "fixed_markup_unreachable",
                            $"El rubro {state.Input.ChargeCode} no puede alcanzar su markup fijo por los límites configurados.",
                            true
                        )
                    );
                }

                state.SuggestedSaleTotalUsd = bounded;
                continue;
            }

            if (state.SuggestedSaleTotalUsd < state.MinimumTotalUsd)
            {
                state.SuggestedSaleTotalUsd = state.MinimumTotalUsd;
            }

            if (
                state.MaximumTotalUsd.HasValue
                && state.SuggestedSaleTotalUsd > state.MaximumTotalUsd.Value
            )
            {
                state.SuggestedSaleTotalUsd = state.MaximumTotalUsd.Value;
            }

            var minimumMarkupTarget =
                state.CostTotalUsd * (1m + (state.Rule.MinimumMarkup / 100m));

            if (
                state.SuggestedSaleTotalUsd + MoneyTolerance
                < minimumMarkupTarget
            )
            {
                issues.Add(
                    new AutoPricingValidationIssue(
                        "minimum_markup_unreachable",
                        $"El rubro {state.Input.ChargeCode} no puede alcanzar el markup mínimo configurado.",
                        true
                    )
                );
            }
        }
    }

    private static void DistributeToTarget(
        IReadOnlyCollection<ChargeState> states,
        decimal targetSaleTotal
    )
    {
        for (var pass = 0; pass < MaxDistributionPasses; pass++)
        {
            var currentTotal = states.Sum(x => x.SuggestedSaleTotalUsd);
            var remaining = targetSaleTotal - currentTotal;

            if (Math.Abs(remaining) <= MoneyTolerance)
            {
                return;
            }

            var increase = remaining > 0m;
            var candidates = states
                .Where(x => CanParticipateInDistribution(x))
                .Select(x => new
                {
                    State = x,
                    Capacity = increase
                        ? GetIncreaseCapacity(x, Math.Abs(remaining))
                        : GetDecreaseCapacity(x),
                })
                .Where(x => x.Capacity > MoneyTolerance)
                .ToArray();

            if (candidates.Length == 0)
            {
                return;
            }

            var weights = candidates
                .Select(x => new
                {
                    x.State,
                    x.Capacity,
                    Weight = ResolveDistributionWeight(x.State),
                })
                .ToArray();

            var totalWeight = weights.Sum(x => x.Weight);
            if (totalWeight <= 0m)
            {
                return;
            }

            var requested = Math.Abs(remaining);
            decimal progress = 0m;

            foreach (var item in weights)
            {
                var share = requested * item.Weight / totalWeight;
                var applied = Math.Min(share, item.Capacity);

                if (applied <= 0m)
                {
                    continue;
                }

                item.State.SuggestedSaleTotalUsd += increase ? applied : -applied;
                progress += applied;
            }

            if (progress <= 0.000001m)
            {
                return;
            }
        }
    }

    private static bool CanParticipateInDistribution(ChargeState state) =>
        !state.IsProtected
        && state.Rule is not null
        && state.Rule.AdjustmentStrategy
            is ChargeAdjustmentStrategy.Priority
                or ChargeAdjustmentStrategy.Proportional
                or ChargeAdjustmentStrategy.CappedMarkup;

    private static decimal ResolveDistributionWeight(ChargeState state)
    {
        if (state.Rule is null)
        {
            return 0m;
        }

        if (state.Rule.AdjustmentStrategy == ChargeAdjustmentStrategy.Proportional)
        {
            return Math.Max(
                1m,
                Math.Max(state.SuggestedSaleTotalUsd, state.CostTotalUsd)
            );
        }

        return Math.Max(1m, state.Rule.AdjustmentPriority);
    }

    private static decimal GetIncreaseCapacity(
        ChargeState state,
        decimal remaining
    )
    {
        if (!state.MaximumTotalUsd.HasValue)
        {
            return remaining;
        }

        return Math.Max(
            0m,
            state.MaximumTotalUsd.Value - state.SuggestedSaleTotalUsd
        );
    }

    private static decimal GetDecreaseCapacity(ChargeState state) =>
        Math.Max(
            0m,
            state.SuggestedSaleTotalUsd - state.MinimumTotalUsd
        );

    private static void QuantizeSuggestedSales(
        IReadOnlyCollection<ChargeState> states
    )
    {
        foreach (var state in states)
        {
            var denominator = state.Input.Quantity * state.Input.CurrencyToUsdRate;
            if (denominator <= 0m)
            {
                continue;
            }

            var nativeUnitSale = state.SuggestedSaleTotalUsd / denominator;
            nativeUnitSale = decimal.Round(
                nativeUnitSale,
                2,
                MidpointRounding.AwayFromZero
            );

            state.SuggestedUnitSaleAmount = Math.Max(0m, nativeUnitSale);
            state.SuggestedSaleTotalUsd = RoundInternal(
                state.SuggestedUnitSaleAmount * denominator
            );
        }
    }

    private static AutoPricingChargeAdjustment ToAdjustment(ChargeState state)
    {
        var adjustment = RoundMoney(
            state.SuggestedSaleTotalUsd - state.OriginalSaleTotalUsd
        );

        return new AutoPricingChargeAdjustment(
            state.Input.RateDetailId,
            state.Input.ChargeCode.Trim(),
            state.Input.CurrencyCode.Trim().ToUpperInvariant(),
            state.Input.Quantity,
            state.Input.SaleAmount,
            state.SuggestedUnitSaleAmount,
            RoundMoney(state.OriginalSaleTotalUsd),
            RoundMoney(state.SuggestedSaleTotalUsd),
            adjustment,
            Math.Abs(adjustment) > MoneyTolerance,
            state.IsProtected,
            state.Rule?.AdjustmentPriority,
            state.Rule?.AdjustmentStrategy,
            state.ProtectionReason
        );
    }

    private static decimal ResolveDesiredSalePrice(
        decimal originalSaleTotal,
        decimal minimumSalePrice,
        decimal? targetMarketPrice
    )
    {
        if (targetMarketPrice.HasValue)
        {
            return RoundMoney(
                Math.Max(minimumSalePrice, targetMarketPrice.Value)
            );
        }

        return RoundMoney(Math.Max(minimumSalePrice, originalSaleTotal));
    }

    private static MarketPositionStatus ResolveMarketPosition(
        bool sampleSufficient,
        decimal suggestedMargin,
        decimal minimumMargin,
        decimal suggestedSale,
        MarketBenchmarkResult benchmark
    )
    {
        if (!sampleSufficient)
        {
            return MarketPositionStatus.InsufficientMarketData;
        }

        if (suggestedMargin + PercentageTolerance < minimumMargin)
        {
            return MarketPositionStatus.BelowMinimumMargin;
        }

        if (
            benchmark.CompetitiveCeiling.HasValue
            && suggestedSale > benchmark.CompetitiveCeiling.Value + MoneyTolerance
        )
        {
            return MarketPositionStatus.AboveCompetitiveRange;
        }

        if (
            benchmark.LowerMarket.HasValue
            && suggestedSale + MoneyTolerance < benchmark.LowerMarket.Value
        )
        {
            return MarketPositionStatus.BelowCompetitiveRange;
        }

        return MarketPositionStatus.Competitive;
    }

    private static AutoPricingApplicationMode ResolveApplicationMode(
        AutoPricingProfile profile,
        decimal confidence,
        bool sampleSufficient
    )
    {
        if (!sampleSufficient)
        {
            return AutoPricingApplicationMode.SuggestOnly;
        }

        if (confidence >= profile.MinimumConfidenceForAutoApply)
        {
            return AutoPricingApplicationMode.AutoApply;
        }

        if (confidence >= profile.MinimumConfidenceForSuggestion)
        {
            return AutoPricingApplicationMode.AutoApplyWithReview;
        }

        return AutoPricingApplicationMode.SuggestOnly;
    }

    private static AutoPricingStatus ResolveAutoPricingStatus(
        bool sampleSufficient,
        MarketPositionStatus marketPosition,
        bool requiresReview
    )
    {
        if (!sampleSufficient)
        {
            return AutoPricingStatus.InsufficientMarketData;
        }

        return marketPosition switch
        {
            MarketPositionStatus.BelowMinimumMargin =>
                AutoPricingStatus.BelowMinimumMargin,
            MarketPositionStatus.AboveCompetitiveRange =>
                AutoPricingStatus.AboveMarket,
            _ when requiresReview => AutoPricingStatus.NeedsReview,
            _ => AutoPricingStatus.Calculated,
        };
    }

    private static decimal CalculateMinimumSalePrice(
        decimal cost,
        decimal minimumMarginPercentage
    )
    {
        if (cost <= 0m)
        {
            return 0m;
        }

        var ratio = minimumMarginPercentage / 100m;
        return RoundMoney(cost / (1m - ratio));
    }

    private static decimal CalculateMargin(decimal cost, decimal sale)
    {
        if (sale <= 0m)
        {
            return 0m;
        }

        return decimal.Round(
            ((sale - cost) / sale) * 100m,
            4,
            MidpointRounding.AwayFromZero
        );
    }

    private static decimal CalculateMarketDeviation(
        decimal suggestedSale,
        decimal? targetMarketPrice
    )
    {
        if (!targetMarketPrice.HasValue || targetMarketPrice.Value <= 0m)
        {
            return 0m;
        }

        return decimal.Round(
            Math.Abs(suggestedSale - targetMarketPrice.Value)
                / targetMarketPrice.Value
                * 100m,
            4,
            MidpointRounding.AwayFromZero
        );
    }

    private static string? ResolveProtectionReason(
        AutoPricingChargeInput charge,
        ChargePricingRule? rule
    )
    {
        if (charge.IsFixedAmount)
        {
            return "fixed_amount";
        }

        if (rule is null)
        {
            return "charge_rule_missing";
        }

        if (!rule.CanAutoAdjust)
        {
            return "auto_adjust_disabled";
        }

        if (rule.AdjustmentStrategy == ChargeAdjustmentStrategy.None)
        {
            return "adjustment_strategy_none";
        }

        return null;
    }

    private static decimal Bound(
        decimal value,
        decimal minimum,
        decimal? maximum
    )
    {
        var result = Math.Max(value, minimum);
        if (maximum.HasValue)
        {
            result = Math.Min(result, maximum.Value);
        }

        return result;
    }

    private static string NormalizeChargeCode(string value)
    {
        var characters = value
            .Trim()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(characters);
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundInternal(decimal value) =>
        decimal.Round(value, 8, MidpointRounding.AwayFromZero);

    private static void ValidateRequest(AutoPricingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.ChargeRules);
        ArgumentNullException.ThrowIfNull(request.Charges);

        if (request.RateId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "RateId es obligatorio para auto pricing."
            );
        }

        if (!request.Profile.IsActive)
        {
            throw new InvalidOperationException(
                "El perfil de auto pricing seleccionado está inactivo."
            );
        }

        if (
            request.MinimumMarginPercentage < 0m
            || request.MinimumMarginPercentage >= 100m
        )
        {
            throw new InvalidOperationException(
                "El margen mínimo debe estar entre 0 y menos de 100."
            );
        }

        if (request.Charges.Count == 0)
        {
            throw new InvalidOperationException(
                "Auto pricing requiere al menos un rubro de costo/venta."
            );
        }

        var duplicatedDetails = request.Charges
            .GroupBy(x => x.RateDetailId)
            .FirstOrDefault(x => x.Key == Guid.Empty || x.Count() > 1);

        if (duplicatedDetails is not null)
        {
            throw new InvalidOperationException(
                "Los detalles de tarifa de auto pricing deben ser únicos y válidos."
            );
        }

        foreach (var charge in request.Charges)
        {
            if (string.IsNullOrWhiteSpace(charge.ChargeCode))
                throw new InvalidOperationException("ChargeCode es obligatorio.");

            if (string.IsNullOrWhiteSpace(charge.CurrencyCode))
                throw new InvalidOperationException("CurrencyCode es obligatorio.");

            if (
                charge.CostAmount < 0m
                || charge.SaleAmount < 0m
                || charge.Quantity <= 0m
                || charge.CurrencyToUsdRate <= 0m
            )
            {
                throw new InvalidOperationException(
                    $"El rubro {charge.ChargeCode} contiene importes, cantidad o conversión inválidos."
                );
            }
        }
    }

    private sealed class ChargeState(
        AutoPricingChargeInput input,
        ChargePricingRule? rule,
        decimal costTotalUsd,
        decimal originalSaleTotalUsd,
        decimal suggestedSaleTotalUsd,
        decimal minimumTotalUsd,
        decimal? maximumTotalUsd,
        bool isProtected,
        string? protectionReason
    )
    {
        public AutoPricingChargeInput Input { get; } = input;
        public ChargePricingRule? Rule { get; } = rule;
        public decimal CostTotalUsd { get; } = costTotalUsd;
        public decimal OriginalSaleTotalUsd { get; } = originalSaleTotalUsd;
        public decimal SuggestedSaleTotalUsd { get; set; } = suggestedSaleTotalUsd;
        public decimal MinimumTotalUsd { get; } = minimumTotalUsd;
        public decimal? MaximumTotalUsd { get; } = maximumTotalUsd;
        public bool IsProtected { get; } = isProtected;
        public string? ProtectionReason { get; } = protectionReason;
        public decimal SuggestedUnitSaleAmount { get; set; } = input.SaleAmount;
    }
}

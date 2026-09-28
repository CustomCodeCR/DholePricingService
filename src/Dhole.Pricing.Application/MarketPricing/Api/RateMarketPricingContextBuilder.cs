using CustomCodeFramework.Core.Results;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.MarketPricing.AutoPricing;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.MarketPricing.Api;

internal sealed class RateMarketPricingContextBuilder(
    IRateHeaderRepository rates,
    IAutoPricingConfigurationRepository configuration
)
{
    public const decimal DefaultMinimumMarginPercentage = 12m;

    public async Task<RateBenchmarkContext> BuildBenchmarkContextAsync(
        Guid rateId,
        DateTime? referenceDate,
        Guid? containerTypeId,
        CancellationToken cancellationToken = default
    )
    {
        var rate = await LoadRateAsync(rateId, cancellationToken);
        var key = BuildMarketKey(rate, containerTypeId);

        return new RateBenchmarkContext(
            rate,
            key,
            ResolveReferenceDate(rate, referenceDate)
        );
    }

    public async Task<RateAutoPricingContext> BuildAutoPricingContextAsync(
        Guid rateId,
        string? profileCode,
        DateTime? referenceDate,
        decimal? minimumMarginPercentage,
        MarketBenchmarkAmountKind amountKind,
        Guid? containerTypeId,
        CancellationToken cancellationToken = default
    )
    {
        var benchmarkContext = await BuildBenchmarkContextAsync(
            rateId,
            referenceDate,
            containerTypeId,
            cancellationToken
        );

        var profile = await configuration.GetActiveProfileAsync(
            profileCode,
            cancellationToken
        );

        if (profile is null)
            throw new MarketPricingContextException(PricingErrors.MarketPricingProfileNotFound);

        var rules = await configuration.GetActiveChargeRulesAsync(cancellationToken);
        var charges = BuildChargeInputs(benchmarkContext.Rate);

        var minimumMargin = minimumMarginPercentage
            ?? DefaultMinimumMarginPercentage;

        if (minimumMargin < 0m || minimumMargin >= 100m)
        {
            throw new MarketPricingContextException(
                PricingErrors.MarketPricingInvalidRequest(
                    "El margen mínimo debe estar entre 0 y menos de 100."
                )
            );
        }

        return new RateAutoPricingContext(
            benchmarkContext.Rate,
            benchmarkContext.Key,
            benchmarkContext.ReferenceDate,
            profile,
            rules,
            charges,
            minimumMargin,
            amountKind
        );
    }

    public static bool MatchesDecisionKey(
        PricingMarketDecision decision,
        MarketComparisonKey key
    ) =>
        decision.ComparisonIncotermId == key.IncotermId
        && decision.ComparisonPolId == key.PolId
        && decision.ComparisonPoeId == key.PoeId
        && decision.ComparisonPodId == key.PodId
        && decision.ComparisonContainerTypeId == key.ContainerTypeId
        && decision.ComparisonMode == key.Mode
        && decision.ComparisonCarrierId == key.CarrierId;

    private async Task<RateHeader> LoadRateAsync(
        Guid rateId,
        CancellationToken cancellationToken
    )
    {
        var rate = await rates.GetByIdWithDetailsAsync(rateId, cancellationToken);

        if (rate is null || rate.IsDeleted)
            throw new MarketPricingContextException(PricingErrors.RateHeaderNotFound);

        return rate;
    }

    private static MarketComparisonKey BuildMarketKey(
        RateHeader rate,
        Guid? requestedContainerTypeId
    )
    {
        if (!rate.IncotermId.HasValue || rate.IncotermId.Value == Guid.Empty)
            throw new MarketPricingContextException(PricingErrors.MarketPricingIncotermRequired);

        Guid? equipmentId = null;

        if (rate.ShipmentMode is ShipmentMode.Fcl or ShipmentMode.Ftl)
        {
            var equipmentIds = rate.RateContainers
                .Where(x => x.Quantity > 0)
                .Select(x => x.ContainerTypeId)
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();

            if (equipmentIds.Length == 0 && rate.ContainerTypeId != Guid.Empty)
                equipmentIds = [rate.ContainerTypeId];

            if (requestedContainerTypeId.HasValue)
            {
                if (!equipmentIds.Contains(requestedContainerTypeId.Value))
                {
                    throw new MarketPricingContextException(
                        PricingErrors.MarketPricingEquipmentNotInRate
                    );
                }

                equipmentId = requestedContainerTypeId.Value;
            }
            else if (equipmentIds.Length == 1)
            {
                equipmentId = equipmentIds[0];
            }
            else if (equipmentIds.Length > 1)
            {
                throw new MarketPricingContextException(
                    PricingErrors.MarketPricingMultipleEquipmentRequiresSelection
                );
            }
            else
            {
                throw new MarketPricingContextException(
                    PricingErrors.MarketPricingEquipmentNotInRate
                );
            }
        }

        return new MarketComparisonKey(
            rate.IncotermId,
            rate.PolId,
            rate.PoeId,
            rate.PodId,
            equipmentId,
            rate.ShipmentMode,
            rate.CarrierId
        );
    }

    private static DateTime ResolveReferenceDate(
        RateHeader rate,
        DateTime? referenceDate
    )
    {
        if (referenceDate.HasValue)
            return NormalizeUtc(referenceDate.Value);

        var now = DateTime.UtcNow;
        return rate.ValidFrom > now ? NormalizeUtc(rate.ValidFrom) : now;
    }

    private static IReadOnlyCollection<AutoPricingChargeInput> BuildChargeInputs(
        RateHeader rate
    )
    {
        if (rate.RateDetails.Count == 0)
        {
            throw new MarketPricingContextException(
                PricingErrors.MarketPricingInvalidRequest(
                    "La tarifa no contiene rubros para calcular auto pricing."
                )
            );
        }

        var result = new List<AutoPricingChargeInput>(rate.RateDetails.Count);

        foreach (var detail in rate.RateDetails)
        {
            var iso = ResolveCurrencyIso(detail.CurrencyCode, detail.CurrencyName);
            var currencyToUsdRate = iso switch
            {
                "USD" => 1m,
                "CRC" => ResolveCrcToUsdRate(rate),
                _ => throw new MarketPricingContextException(
                    PricingErrors.MarketPricingUnsupportedCurrency(
                        string.IsNullOrWhiteSpace(iso)
                            ? detail.CurrencyCode
                            : iso
                    )
                ),
            };

            result.Add(
                new AutoPricingChargeInput(
                    detail.Id,
                    detail.Name,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Quantity,
                    iso,
                    currencyToUsdRate,
                    detail.CostType == CostType.Fixed
                )
            );
        }

        return result;
    }

    private static decimal ResolveCrcToUsdRate(RateHeader rate)
    {
        var usdCrcRate = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied.Value
            : rate.ExchangeRateSale is > 0m
                ? rate.ExchangeRateSale.Value
                : 0m;

        if (usdCrcRate <= 0m)
        {
            throw new MarketPricingContextException(
                PricingErrors.MarketPricingUnsupportedCurrency("CRC")
            );
        }

        return 1m / usdCrcRate;
    }

    private static string ResolveCurrencyIso(string? code, string? name)
    {
        static string Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();

        var normalizedCode = Normalize(code);
        var normalizedName = Normalize(name);

        if (normalizedCode is "USD" or "CRC")
            return normalizedCode;

        if (normalizedName.Contains("USD", StringComparison.Ordinal))
            return "USD";

        if (
            normalizedName.Contains("CRC", StringComparison.Ordinal)
            || normalizedName.Contains("COLON", StringComparison.Ordinal)
            || normalizedName.Contains("COLÓN", StringComparison.Ordinal)
        )
        {
            return "CRC";
        }

        return normalizedCode;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}

internal sealed record RateBenchmarkContext(
    RateHeader Rate,
    MarketComparisonKey Key,
    DateTime ReferenceDate
);

internal sealed record RateAutoPricingContext(
    RateHeader Rate,
    MarketComparisonKey Key,
    DateTime ReferenceDate,
    AutoPricingProfile Profile,
    IReadOnlyCollection<ChargePricingRule> ChargeRules,
    IReadOnlyCollection<AutoPricingChargeInput> Charges,
    decimal MinimumMarginPercentage,
    MarketBenchmarkAmountKind BenchmarkAmountKind
)
{
    public AutoPricingRequest ToRequest() =>
        new(
            Rate.Id,
            Key,
            ReferenceDate,
            MinimumMarginPercentage,
            Profile,
            ChargeRules,
            Charges,
            BenchmarkAmountKind
        );
}

internal sealed class MarketPricingContextException(Error error)
    : InvalidOperationException(error.Message)
{
    public Error Error { get; } = error;
}

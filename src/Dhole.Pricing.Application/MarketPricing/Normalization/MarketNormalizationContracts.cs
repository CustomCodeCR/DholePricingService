using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.MarketPricing.Normalization;

public sealed record RawMarketCharge(
    string Name,
    decimal Amount,
    string? Currency = null,
    string? RawBasis = null,
    MarketChargeCategory? CategoryHint = null,
    bool IsAllInTotal = false
);

public sealed record MarketRateNormalizationRequest(
    Guid? IncotermId,
    string? Incoterm,
    Guid? PolId,
    string? Pol,
    Guid? PoeId,
    string? Poe,
    Guid? PodId,
    string? Pod,
    Guid? CarrierId,
    string? Carrier,
    Guid? EquipmentId,
    string? Equipment,
    string? RawMode,
    ShipmentMode? ModeHint,
    string Currency,
    int Quantity,
    MarketRateBasis? RateBasisHint,
    string? RawRateBasis,
    decimal? OceanFreight,
    decimal? OriginCharges,
    decimal? DestinationCharges,
    decimal? InlandCharges,
    decimal? OtherCharges,
    decimal? TotalAmount,
    decimal? VolumeCbm,
    decimal? WeightKg,
    decimal? ChargeableWeightKg,
    decimal? ExplicitExchangeRateToUsd,
    DateTime? ExplicitExchangeRateDate,
    IReadOnlyCollection<RawMarketCharge>? Charges,
    DateTime? ReferenceDate = null
);

public sealed record MarketCatalogMatch(
    string CatalogGroupSlug,
    string? RawValue,
    Guid? Id,
    string? Name,
    string? Code,
    decimal Confidence,
    bool Matched,
    bool Ambiguous = false
);

public sealed record NormalizedMarketRoute(
    MarketCatalogMatch Pol,
    MarketCatalogMatch Poe,
    MarketCatalogMatch Pod
);

public sealed record MarketCurrencyNormalization(
    string OriginalCurrency,
    string NormalizedCurrency,
    decimal? RateToUsd,
    DateTime? ExchangeRateDate,
    string? ExchangeRateSource,
    decimal Confidence,
    bool Converted
);

public sealed record ConvertedMarketCharge(
    string Name,
    MarketChargeCategory Category,
    MarketRateBasis SourceBasis,
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal AmountUsd,
    decimal CurrencyRateToUsd,
    bool IsAllInTotal
);

public sealed record NormalizedMarketCharge(
    string Name,
    MarketChargeCategory Category,
    MarketRateBasis SourceBasis,
    MarketRateBasis TargetBasis,
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal NormalizedAmountUsd,
    decimal CurrencyRateToUsd,
    decimal BasisConfidence,
    bool IsAllInTotal
);

public sealed record MarketStrategyNormalizationContext(
    MarketRateNormalizationRequest Request,
    MarketCatalogMatch Equipment,
    IReadOnlyCollection<ConvertedMarketCharge> Charges
);

public sealed record MarketStrategyNormalizationResult(
    MarketRateBasis TargetBasis,
    IReadOnlyCollection<NormalizedMarketCharge> Charges,
    decimal BasisConfidence,
    IReadOnlyCollection<MarketNormalizationIssue> Issues
);

public sealed record MarketNormalizationIssue(
    string Code,
    string Message,
    bool IsBlocking = false
);

public sealed record MarketRateNormalizationResult(
    bool Success,
    ShipmentMode? Mode,
    MarketCatalogMatch Incoterm,
    NormalizedMarketRoute Route,
    MarketCatalogMatch Equipment,
    MarketCatalogMatch Carrier,
    MarketCurrencyNormalization Currency,
    MarketRateBasis TargetBasis,
    decimal? NormalizedOceanFreight,
    decimal? NormalizedOriginCharges,
    decimal? NormalizedDestinationCharges,
    decimal? NormalizedInlandCharges,
    decimal? NormalizedOtherCharges,
    decimal? NormalizedAllIn,
    decimal NormalizationConfidence,
    IReadOnlyCollection<NormalizedMarketCharge> Charges,
    IReadOnlyCollection<MarketNormalizationIssue> Issues
);

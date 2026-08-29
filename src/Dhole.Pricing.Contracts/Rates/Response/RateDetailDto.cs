namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateDetailDto(
    Guid Id,
    Guid RateHeaderId,
    Guid? CostId,
    string Name,
    string CostDetailType,
    string CostType,
    string ChargeBasis,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    decimal UtilityAmount,
    decimal Quantity,
    string? Notes,
    bool ApplyDestinationTax,
    decimal DestinationTaxRate,
    decimal DestinationTaxAmount
);

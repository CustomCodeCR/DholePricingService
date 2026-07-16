namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateDetailDto(
    Guid Id,
    Guid RateHeaderId,
    Guid? CostId,
    string Name,
    string CostDetailType,
    string CostType,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    decimal UtilityAmount,
    int Quantity,
    string? Notes
);

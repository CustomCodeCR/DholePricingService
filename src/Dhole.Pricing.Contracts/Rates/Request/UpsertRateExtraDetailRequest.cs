namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record UpsertRateExtraDetailRequest(
    Guid? Id,
    Guid? CostId,
    string Name,
    string CostDetailType,
    string CostType,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    string? Notes
);

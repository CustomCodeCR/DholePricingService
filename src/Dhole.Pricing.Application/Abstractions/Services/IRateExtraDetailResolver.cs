using CustomCodeFramework.Core.Results;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Application.Abstractions.Services;

public sealed record RateExtraDetailInput(
    Guid? Id,
    Guid? CostId,
    string Name,
    CostDetailType CostDetailType,
    CostType CostType,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    string? Notes
);

public sealed record ResolvedRateExtraDetail(
    Guid? Id,
    Guid? CostId,
    string Name,
    CostDetailType CostDetailType,
    CostType CostType,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    string? Notes,
    bool IsAccountant
);

public sealed record RateExtraDetailResolution(ResolvedRateExtraDetail? Detail, Error? Error)
{
    public bool IsSuccess => Error is null;

    public static RateExtraDetailResolution Success(ResolvedRateExtraDetail detail)
    {
        return new RateExtraDetailResolution(detail, null);
    }

    public static RateExtraDetailResolution Failure(Error error)
    {
        return new RateExtraDetailResolution(null, error);
    }
}

public interface IRateExtraDetailResolver
{
    Task<RateExtraDetailResolution> ResolveAsync(
        RateExtraDetailInput input,
        CancellationToken cancellationToken = default
    );
}

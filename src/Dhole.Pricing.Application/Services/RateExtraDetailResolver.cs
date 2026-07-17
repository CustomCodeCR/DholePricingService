using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Services;

public sealed class RateExtraDetailResolver(ICostRepository costs) : IRateExtraDetailResolver
{
    public async Task<RateExtraDetailResolution> ResolveAsync(
        RateExtraDetailInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return RateExtraDetailResolution.Failure(PricingErrors.RateCostDetailNameIsRequired);
        }

        if (input.CostAmount < 0m || input.SaleAmount < 0m)
        {
            return RateExtraDetailResolution.Failure(
                PricingErrors.RateCostDetailAmountMustBeGreaterOrEqualThanZero
            );
        }

        if (input.CurrencyId == Guid.Empty)
        {
            return RateExtraDetailResolution.Failure(
                PricingErrors.RateCostDetailCurrencyIsRequired
            );
        }

        if (
            string.IsNullOrWhiteSpace(input.CurrencyName)
            || string.IsNullOrWhiteSpace(input.CurrencyCode)
        )
        {
            return RateExtraDetailResolution.Failure(
                PricingErrors.RateCostDetailCurrencySnapshotIsRequired
            );
        }

        /*
         * CostId null representa un costo manual.
         * Los costos manuales no pueden declararse como Fixed,
         * porque Fixed está reservado para costos automáticos.
         */
        if (!input.CostId.HasValue)
        {
            if (input.CostType == CostType.Fixed)
            {
                return RateExtraDetailResolution.Failure(PricingErrors.RateCostDetailFixedLocked);
            }

            return RateExtraDetailResolution.Success(
                new ResolvedRateExtraDetail(
                    input.Id,
                    CostId: null,
                    input.Name.Trim(),
                    input.CostDetailType,
                    input.CostType,
                    input.CurrencyId,
                    input.CurrencyName.Trim(),
                    input.CurrencyCode.Trim(),
                    input.CostAmount,
                    input.SaleAmount,
                    Normalize(input.Notes),
                    IsAccountant: false
                )
            );
        }

        var cost = await costs.GetByIdAsync(input.CostId.Value, cancellationToken);

        if (cost is null)
        {
            return RateExtraDetailResolution.Failure(PricingErrors.CostNotFound);
        }

        if (cost.CostType == CostType.Fixed)
        {
            if (!input.Id.HasValue)
            {
                return RateExtraDetailResolution.Failure(PricingErrors.RateCostDetailFixedLocked);
            }

            var saleAmount = cost.AgentId.HasValue ? 0m : input.SaleAmount;

            return RateExtraDetailResolution.Success(
                new ResolvedRateExtraDetail(
                    input.Id,
                    cost.Id,
                    cost.Name,
                    cost.CostDetailType,
                    cost.CostType,
                    cost.CurrencyId,
                    cost.CurrencyName,
                    cost.CurrencyCode,
                    input.CostAmount,
                    saleAmount,
                    Normalize(input.Notes) ?? cost.Notes,
                    cost.IsAccountant
                )
            );
        }

        if (cost.IsDeleted)
        {
            return RateExtraDetailResolution.Failure(PricingErrors.CostNotFound);
        }

        if (!cost.IsActive)
        {
            return RateExtraDetailResolution.Failure(PricingErrors.CostIsInactive);
        }

        return RateExtraDetailResolution.Success(
            new ResolvedRateExtraDetail(
                input.Id,
                cost.Id,
                cost.Name,
                cost.CostDetailType,
                cost.CostType,
                cost.CurrencyId,
                cost.CurrencyName,
                cost.CurrencyCode,
                input.CostAmount,
                input.SaleAmount,
                Normalize(input.Notes) ?? cost.Notes,
                cost.IsAccountant
            )
        );
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

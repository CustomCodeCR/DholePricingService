using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Shared;
using System.Globalization;
using System.Text.RegularExpressions;

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

        if (input.Quantity is <= 0)
        {
            return RateExtraDetailResolution.Failure(PricingErrors.RateInvalidStatus);
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

            var (costAmount, saleAmount) = ResolveGeneratedInsuranceAmounts(input);

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
                    costAmount,
                    saleAmount,
                    Normalize(input.Notes),
                    IsAccountant: false,
                    input.Quantity,
                    input.ChargeBasis
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
            // En creación se permite enviar el costo fijo para personalizar únicamente su venta.
            // El costo contable siempre se toma del maestro; así el cliente no puede alterarlo.
            if (!input.Id.HasValue && (cost.IsDeleted || !cost.IsActive))
            {
                return RateExtraDetailResolution.Failure(
                    cost.IsDeleted ? PricingErrors.CostNotFound : PricingErrors.CostIsInactive
                );
            }

            var costAmount = input.Id.HasValue ? input.CostAmount : cost.CostAmount;
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
                    costAmount,
                    saleAmount,
                    Normalize(input.Notes) ?? cost.Notes,
                    cost.IsAccountant,
                    input.Quantity,
                    cost.ChargeBasis
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
                cost.IsAccountant,
                input.Quantity,
                cost.ChargeBasis
            )
        );
    }

    private static (decimal CostAmount, decimal SaleAmount) ResolveGeneratedInsuranceAmounts(
        RateExtraDetailInput input
    )
    {
        if (
            input.CostDetailType != CostDetailType.Insurance
            || string.IsNullOrWhiteSpace(input.Notes)
            || !input.Notes.StartsWith(
                "Seguro de carga · valor carga USD",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return (input.CostAmount, input.SaleAmount);
        }

        var cargoValue = ReadDecimal(input.Notes, @"valor carga USD\s+([0-9]+(?:[.,][0-9]+)?)");
        if (!cargoValue.HasValue || cargoValue.Value <= 0m)
        {
            return (input.CostAmount, input.SaleAmount);
        }

        var salePercentage =
            ReadDecimal(input.Notes, @"·\s*([0-9]+(?:[.,][0-9]+)?)%")
            ?? CargoInsurancePricingRules.DefaultSalePercentage;
        var saleMinimum =
            ReadDecimal(input.Notes, @"mínimo USD\s+([0-9]+(?:[.,][0-9]+)?)")
            ?? CargoInsurancePricingRules.DefaultSaleMinimumAmount;
        decimal? manualSaleAmount = input.Notes.Contains(
            "tarifa manual",
            StringComparison.OrdinalIgnoreCase
        )
            ? input.SaleAmount
            : null;

        var calculated = CargoInsurancePricingRules.Calculate(
            cargoValue.Value,
            salePercentage,
            saleMinimum,
            manualSaleAmount
        );

        return (calculated.CostAmount, calculated.SaleAmount);
    }

    private static decimal? ReadDecimal(string source, string pattern)
    {
        var match = Regex.Match(
            source,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!match.Success || match.Groups.Count < 2) return null;

        var raw = match.Groups[1].Value.Replace(',', '.');
        return decimal.TryParse(
            raw,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Features.Rates;

internal sealed record RateRevisionSnapshotData(
    string Status, string RateName, string? IdtraNumber, string? QuoNumber,
    decimal TotalSaleUsd, decimal TotalSaleCrc, decimal MarginPercentage, string Json);

internal static class RateRevisionSnapshotFactory
{
    private sealed record RevisionTotals(
        decimal CostUsd, decimal SaleUsd, decimal UtilityUsd,
        decimal CostCrc, decimal SaleCrc, decimal UtilityCrc,
        decimal MarginPercentage);

    public static RateRevisionSnapshotData Capture(RateHeader rate)
    {
        var totals = CalculateTotals(rate);
        var json = JsonSerializer.Serialize(new
        {
            rate.Id, rate.RateCode, rate.RateName, rate.RevisionNumber, rate.Status,
            rate.ClientName, rate.ExecutiveName, rate.IdtraNumber, rate.QuoNumber,
            rate.AgentId, rate.AgentName, rate.AgentCode, rate.CarrierId, rate.CarrierName, rate.CarrierCode,
            rate.PolId, rate.PolName, rate.PolCode, rate.PoeId, rate.PoeName, rate.PoeCode,
            rate.PodId, rate.PodName, rate.PodCode, rate.ContainerTypeId, rate.ContainerTypeName, rate.ContainerTypeCode,
            rate.IncotermId, rate.IncotermName, rate.IncotermCode, rate.WarehouseId, rate.PickupAddress, rate.PickupLatitude, rate.PickupLongitude,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode, rate.ExchangeRatePurchase, rate.ExchangeRateSale,
            rate.ExchangeRateApplied, rate.ExchangeRateDate, rate.ExchangeRateSource, rate.FreeDays, rate.ValidFrom, rate.ValidTo,
            rate.ContainerQuantity, rate.ShipmentMode, rate.OperationType, rate.TotalPackages, rate.TotalPallets,
            rate.TotalWeightKg, rate.TotalVolumeCbm, rate.KgPerCbm, rate.ChargeableQuantity, rate.CargoLinesJson,
            rate.Includes, rate.SubjectTo, rate.Excludes, rate.TransitTime, rate.RateType,
            TotalCostAmount = ResolveHeaderAmount(rate, totals.CostUsd, totals.CostCrc),
            TotalSaleAmount = ResolveHeaderAmount(rate, totals.SaleUsd, totals.SaleCrc),
            TotalUtilityAmount = ResolveHeaderAmount(rate, totals.UtilityUsd, totals.UtilityCrc),
            TotalCostUsd = totals.CostUsd, TotalSaleUsd = totals.SaleUsd, TotalUtilityUsd = totals.UtilityUsd,
            TotalCostCrc = totals.CostCrc, TotalSaleCrc = totals.SaleCrc, TotalUtilityCrc = totals.UtilityCrc,
            MarginPercentage = totals.MarginPercentage, rate.RequiredApproval,
            Containers = rate.RateContainers.Select(x => new { x.ContainerTypeId, x.ContainerTypeName, x.ContainerTypeCode, x.Quantity }),
            Services = rate.RateServices.Select(x => new { x.ServiceId, x.ServiceName, x.ServiceCode }),
            Details = rate.RateDetails.Select(x => new { x.Id, x.CostId, x.Name, x.CostDetailType, x.CostType, x.ChargeBasis,
                x.CurrencyId, x.CurrencyName, x.CurrencyCode, x.CostAmount, x.SaleAmount, x.UtilityAmount, x.Quantity, x.Notes, x.ApplyDestinationTax, x.DestinationTaxRate, x.DestinationTaxAmount })
        });
        return new(rate.Status.ToString(), rate.RateName, rate.IdtraNumber, rate.QuoNumber,
            totals.SaleUsd, totals.SaleCrc, totals.MarginPercentage, json);
    }

    private static RevisionTotals CalculateTotals(RateHeader rate)
    {
        var exchangeRate = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied.Value
            : rate.ExchangeRateSale.GetValueOrDefault();

        decimal costUsd = 0m, saleUsd = 0m, costCrc = 0m, saleCrc = 0m;

        foreach (var detail in rate.RateDetails)
        {
            var quantity = detail.Quantity <= 0m ? 1m : detail.Quantity;
            var cost = detail.CostAmount * quantity;
            var sale = detail.SaleAmount * quantity;
            var currency = ResolveCurrencyIso(detail.CurrencyCode, detail.CurrencyName);

            if (currency == "CRC")
            {
                costCrc += cost;
                saleCrc += sale;
                if (exchangeRate > 0m)
                {
                    costUsd += cost / exchangeRate;
                    saleUsd += sale / exchangeRate;
                }
            }
            else
            {
                // Pricing actualmente convierte USD/CRC. Para códigos internos como
                // CUR-2026-001 usamos CurrencyName, y cualquier moneda no reconocida
                // conserva compatibilidad tratándose como la moneda base USD.
                costUsd += cost;
                saleUsd += sale;
                if (exchangeRate > 0m)
                {
                    costCrc += cost * exchangeRate;
                    saleCrc += sale * exchangeRate;
                }
            }
        }

        costUsd = decimal.Round(costUsd, 2, MidpointRounding.AwayFromZero);
        saleUsd = decimal.Round(saleUsd, 2, MidpointRounding.AwayFromZero);
        costCrc = decimal.Round(costCrc, 2, MidpointRounding.AwayFromZero);
        saleCrc = decimal.Round(saleCrc, 2, MidpointRounding.AwayFromZero);

        var utilityUsd = saleUsd - costUsd;
        var utilityCrc = saleCrc - costCrc;
        var margin = saleUsd <= 0m ? 0m : utilityUsd / saleUsd * 100m;

        return new RevisionTotals(costUsd, saleUsd, utilityUsd, costCrc, saleCrc, utilityCrc, margin);
    }

    private static decimal ResolveHeaderAmount(RateHeader rate, decimal usd, decimal crc)
    {
        var currency = ResolveCurrencyIso(rate.CurrencyCode, rate.CurrencyName);
        return currency == "CRC" ? crc : usd;
    }

    private static string ResolveCurrencyIso(string? code, string? name)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToUpperInvariant();
        var normalizedName = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim().ToUpperInvariant();

        if (normalizedCode is "USD" or "CRC") return normalizedCode;
        if (normalizedName.Contains("CRC", StringComparison.Ordinal)
            || normalizedName.Contains("COLON", StringComparison.Ordinal)
            || normalizedName.Contains("COLÓN", StringComparison.Ordinal))
            return "CRC";
        return "USD";
    }
}

using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Features.Rates;

internal sealed record RateRevisionSnapshotData(
    string Status, string RateName, string? IdtraNumber, string? QuoNumber,
    decimal TotalSaleUsd, decimal TotalSaleCrc, decimal MarginPercentage, string Json);

internal static class RateRevisionSnapshotFactory
{
    public static RateRevisionSnapshotData Capture(RateHeader rate)
    {
        var json = JsonSerializer.Serialize(new
        {
            rate.Id, rate.RateCode, rate.RateName, rate.RevisionNumber, rate.Status,
            rate.ClientName, rate.ExecutiveName, rate.IdtraNumber, rate.QuoNumber,
            rate.AgentId, rate.AgentName, rate.AgentCode, rate.CarrierId, rate.CarrierName, rate.CarrierCode,
            rate.PolId, rate.PolName, rate.PolCode, rate.PoeId, rate.PoeName, rate.PoeCode,
            rate.PodId, rate.PodName, rate.PodCode, rate.ContainerTypeId, rate.ContainerTypeName, rate.ContainerTypeCode,
            rate.IncotermId, rate.IncotermName, rate.IncotermCode, rate.PickupAddress, rate.PickupLatitude, rate.PickupLongitude,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode, rate.ExchangeRatePurchase, rate.ExchangeRateSale,
            rate.ExchangeRateApplied, rate.ExchangeRateDate, rate.ExchangeRateSource, rate.FreeDays, rate.ValidFrom, rate.ValidTo,
            rate.ContainerQuantity, rate.ShipmentMode, rate.OperationType, rate.TotalPackages, rate.TotalPallets,
            rate.TotalWeightKg, rate.TotalVolumeCbm, rate.KgPerCbm, rate.ChargeableQuantity, rate.CargoLinesJson,
            rate.Includes, rate.SubjectTo, rate.Excludes, rate.TransitTime, rate.RateType,
            rate.TotalCostAmount, rate.TotalSaleAmount, rate.TotalUtilityAmount,
            rate.TotalCostUsd, rate.TotalSaleUsd, rate.TotalUtilityUsd, rate.TotalCostCrc, rate.TotalSaleCrc, rate.TotalUtilityCrc,
            rate.MarginPercentage, rate.RequiredApproval,
            Containers = rate.RateContainers.Select(x => new { x.ContainerTypeId, x.ContainerTypeName, x.ContainerTypeCode, x.Quantity }),
            Services = rate.RateServices.Select(x => new { x.ServiceId, x.ServiceName, x.ServiceCode }),
            Details = rate.RateDetails.Select(x => new { x.Id, x.CostId, x.Name, x.CostDetailType, x.CostType, x.ChargeBasis,
                x.CurrencyId, x.CurrencyName, x.CurrencyCode, x.CostAmount, x.SaleAmount, x.UtilityAmount, x.Quantity, x.Notes })
        });
        return new(rate.Status.ToString(), rate.RateName, rate.IdtraNumber, rate.QuoNumber,
            rate.TotalSaleUsd, rate.TotalSaleCrc, rate.MarginPercentage, json);
    }
}

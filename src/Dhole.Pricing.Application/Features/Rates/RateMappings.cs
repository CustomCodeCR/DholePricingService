using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Features.Rates;

internal static class RateMappings
{
    public static RateDto ToDto(this RateHeader rate)
    {
        return new RateDto(
            rate.Id,
            rate.RateCode,
            rate.RateName,
            rate.RevisionNumber,
            rate.SourceImportFclRateId,
            rate.AgentId,
            rate.AgentName,
            rate.AgentCode,
            rate.CarrierId,
            rate.CarrierName,
            rate.CarrierCode,
            rate.PolId,
            rate.PolName,
            rate.PolCode,
            rate.PoeId,
            rate.PoeName,
            rate.PoeCode,
            rate.PodId,
            rate.PodName,
            rate.PodCode,
            rate.ContainerTypeId,
            rate.ContainerTypeName,
            rate.ContainerTypeCode,
            rate.IncotermId,
            rate.IncotermName,
            rate.IncotermCode,
            rate.PickupAddress,
            rate.PickupLatitude,
            rate.PickupLongitude,
            rate.ContainerQuantity,
            rate.CurrencyId,
            rate.CurrencyName,
            rate.CurrencyCode,
            rate.ExchangeRatePurchase,
            rate.ExchangeRateSale,
            rate.ExchangeRateApplied,
            rate.ExchangeRateDate,
            rate.ExchangeRateCapturedAtUtc,
            rate.ExchangeRateSource,
            rate.ExchangeRateManualOverride,
            rate.FreeDays,
            rate.ValidFrom,
            rate.ValidTo,
            rate.ClientName,
            rate.ExecutiveName,
            rate.IdtraNumber,
            rate.QuoNumber,
            rate.Includes,
            rate.SubjectTo,
            rate.Excludes,
            rate.TransitTime,
            rate.RateType.ToString(),
            rate.ShipmentMode.ToString(),
            rate.OperationType.ToString(),
            rate.TotalPackages,
            rate.TotalPallets,
            rate.TotalWeightKg,
            rate.TotalVolumeCbm,
            rate.KgPerCbm,
            rate.ChargeableQuantity,
            RateCargoProfileFactory.Deserialize(rate.CargoLinesJson),
            rate.TotalCostAmount,
            rate.TotalSaleAmount,
            rate.TotalUtilityAmount,
            rate.TotalCostUsd,
            rate.TotalSaleUsd,
            rate.TotalUtilityUsd,
            rate.TotalCostCrc,
            rate.TotalSaleCrc,
            rate.TotalUtilityCrc,
            rate.MarginPercentage,
            rate.RequiredApproval,
            rate.Status.ToString(),
            rate.ClosedReason,
            rate.ClosedAtUtc,
            rate.ClosedBy,
            (rate.RateContainers.Count > 0
                ? rate.RateContainers
                    .OrderBy(x => x.ContainerTypeName)
                    .ThenBy(x => x.ContainerTypeCode)
                    .Select(x => new RateContainerDto(
                        x.Id,
                        x.RateHeaderId,
                        x.ContainerTypeId,
                        x.ContainerTypeName,
                        x.ContainerTypeCode,
                        x.Quantity
                    ))
                : new[]
                {
                    new RateContainerDto(
                        Guid.Empty,
                        rate.Id,
                        rate.ContainerTypeId,
                        rate.ContainerTypeName,
                        rate.ContainerTypeCode,
                        rate.ContainerQuantity
                    )
                })
                .ToList(),
            rate.RateDetails.OrderBy(x => x.CostDetailType)
                .ThenBy(x => x.Name)
                .Select(x => new RateDetailDto(
                    x.Id,
                    x.RateHeaderId,
                    x.CostId,
                    x.Name,
                    x.CostDetailType.ToString(),
                    x.CostType.ToString(),
                    x.ChargeBasis.ToString(),
                    x.CurrencyId,
                    x.CurrencyName,
                    x.CurrencyCode,
                    x.CostAmount,
                    x.SaleAmount,
                    x.UtilityAmount,
                    x.Quantity,
                    x.Notes
                ))
                .ToList(),
            rate.RateServices
                .OrderBy(x => x.ServiceName)
                .Select(x => new RateServiceDto(x.ServiceId, x.ServiceName, x.ServiceCode))
                .ToList()
        );
    }
}

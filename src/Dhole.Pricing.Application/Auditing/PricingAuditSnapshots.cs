using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Auditing;

public static class PricingAuditSnapshots
{
    public static object From(Cost cost) =>
        new
        {
            cost.Id,
            cost.Name,

            CostType = cost.CostType.ToString(),
            CostDetailType = cost.CostDetailType.ToString(),

            cost.CarrierId,
            cost.CarrierName,
            cost.CarrierCode,

            cost.AgentId,
            cost.AgentName,
            cost.AgentCode,

            cost.PortId,
            cost.PortName,
            cost.PortCode,
            PortRole = cost.PortRole.ToString(),

            cost.PolId,
            cost.PolName,
            cost.PolCode,
            cost.PoeId,
            cost.PoeName,
            cost.PoeCode,
            cost.PodId,
            cost.PodName,
            cost.PodCode,

            Incoterms = cost.Incoterms
                .OrderBy(x => x.IncotermName)
                .Select(x => new { x.IncotermId, x.IncotermName, x.IncotermCode })
                .ToArray(),

            cost.CurrencyId,
            cost.CurrencyName,
            cost.CurrencyCode,

            cost.CostAmount,
            cost.SaleAmount,
            cost.UtilityAmount,
            ShipmentMode = cost.ShipmentMode?.ToString(),
            ChargeBasis = cost.ChargeBasis.ToString(),
            cost.MinimumCostAmount,
            cost.MinimumSaleAmount,
            cost.KgPerCbm,

            cost.Notes,
            cost.IsAccountant,
            cost.IsActive,
        };

    public static object From(ImportFclRates importFclRate) =>
        new
        {
            importFclRate.Id,
            importFclRate.ImportBatchId,

            SourceType = importFclRate.SourceType.ToString(),

            importFclRate.PolId,
            importFclRate.Pol,
            importFclRate.PolName,
            importFclRate.PolCode,
            importFclRate.PolSlug,

            importFclRate.PoeId,
            importFclRate.Poe,
            importFclRate.PoeName,
            importFclRate.PoeCode,
            importFclRate.PoeSlug,

            importFclRate.PodId,
            importFclRate.Pod,
            importFclRate.PodName,
            importFclRate.PodCode,
            importFclRate.PodSlug,

            importFclRate.CarrierId,
            importFclRate.Carrier,
            importFclRate.CarrierName,
            importFclRate.AgentId,
            importFclRate.Agent,
            importFclRate.AgentName,
            importFclRate.ContainerTypeId,
            importFclRate.ContainerType,
            importFclRate.ContainerTypeName,
            importFclRate.CurrencyId,
            importFclRate.Currency,
            importFclRate.CurrencyName,

            importFclRate.Commodity,
            importFclRate.SpaceComment,
            importFclRate.Freight,
            importFclRate.OceanFreight,
            importFclRate.OriginCharges,
            importFclRate.DestinationCharges,
            importFclRate.Surcharges,
            importFclRate.TotalCost,
            importFclRate.TotalSale,
            importFclRate.Profit,
            importFclRate.Margin,
            importFclRate.FreeDays,
            importFclRate.TransitDays,
            importFclRate.ValidFrom,
            importFclRate.ValidTo,

            Status = importFclRate.Status.ToString(),

            importFclRate.RawDataJson,
            importFclRate.SourceUrl,
            importFclRate.UsedAsRateCount,
            importFclRate.CreatedAsRateHeaderId,
        };

    public static object From(RateHeader rateHeader) =>
        new
        {
            rateHeader.Id,
            rateHeader.RateCode,
            rateHeader.RateName,

            rateHeader.SourceImportFclRateId,

            rateHeader.AgentId,
            rateHeader.AgentName,
            rateHeader.AgentCode,

            rateHeader.CarrierId,
            rateHeader.CarrierName,
            rateHeader.CarrierCode,

            rateHeader.PolId,
            rateHeader.PolName,
            rateHeader.PolCode,

            rateHeader.PoeId,
            rateHeader.PoeName,
            rateHeader.PoeCode,

            rateHeader.PodId,
            rateHeader.PodName,
            rateHeader.PodCode,

            rateHeader.ContainerTypeId,
            rateHeader.ContainerTypeName,
            rateHeader.ContainerTypeCode,
            rateHeader.ContainerQuantity,
            ShipmentMode = rateHeader.ShipmentMode.ToString(),
            rateHeader.TotalPackages,
            rateHeader.TotalPallets,
            rateHeader.TotalWeightKg,
            rateHeader.TotalVolumeCbm,
            rateHeader.KgPerCbm,
            rateHeader.ChargeableQuantity,
            rateHeader.CargoLinesJson,

            rateHeader.IncotermId,
            rateHeader.IncotermName,
            rateHeader.IncotermCode,

            rateHeader.CurrencyId,
            rateHeader.CurrencyName,
            rateHeader.CurrencyCode,
            rateHeader.ExchangeRatePurchase,
            rateHeader.ExchangeRateSale,
            rateHeader.ExchangeRateApplied,
            rateHeader.ExchangeRateDate,
            rateHeader.ExchangeRateCapturedAtUtc,
            rateHeader.ExchangeRateSource,
            rateHeader.ExchangeRateManualOverride,

            rateHeader.FreeDays,
            rateHeader.ValidFrom,
            rateHeader.ValidTo,
            rateHeader.ClientName,
            rateHeader.IdtraNumber,
            rateHeader.QuoNumber,
            rateHeader.Includes,
            rateHeader.SubjectTo,
            rateHeader.Excludes,
            rateHeader.TransitTime,
            RateType = rateHeader.RateType.ToString(),

            rateHeader.TotalCostAmount,
            rateHeader.TotalSaleAmount,
            rateHeader.TotalUtilityAmount,
            rateHeader.MarginPercentage,
            rateHeader.RequiredApproval,

            Status = rateHeader.Status.ToString(),

            Containers = rateHeader.RateContainers.Select(x => new
            {
                x.Id,
                x.ContainerTypeId,
                x.ContainerTypeName,
                x.ContainerTypeCode,
                x.Quantity,
            }).ToList(),
            RateDetails = rateHeader.RateDetails.Select(From).ToList(),
        };

    public static object From(RateDetail detail) =>
        new
        {
            detail.Id,
            detail.RateHeaderId,
            detail.CostId,

            detail.Name,

            CostDetailType = detail.CostDetailType.ToString(),
            CostType = detail.CostType.ToString(),
            ChargeBasis = detail.ChargeBasis.ToString(),

            detail.CurrencyId,
            detail.CurrencyName,
            detail.CurrencyCode,

            detail.CostAmount,
            detail.SaleAmount,
            detail.UtilityAmount,
            detail.Quantity,

            detail.Notes,
        };
}

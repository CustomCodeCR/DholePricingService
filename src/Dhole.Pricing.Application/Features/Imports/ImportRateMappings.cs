using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Entities;

namespace Dhole.Pricing.Application.Features.Imports;

internal static class ImportRateMappings
{
    public static ImportRateDto ToDto(this ImportFclRates importRate)
    {
        return new ImportRateDto
        {
            Id = importRate.Id,
            ImportBatchId = importRate.ImportBatchId,
            ExtractionRecordId = importRate.ExtractionRecordId,
            SourceType = importRate.SourceType.ToString(),
            ShipmentMode = ResolveShipmentMode(importRate),
            ImportProfileId = importRate.ImportProfileId,
            ImportProfileName = importRate.ImportProfileName,
            ImportProfileCode = importRate.ImportProfileCode,
            ImportProfileSlug = importRate.ImportProfileSlug,
            PolId = importRate.PolId,
            Pol = importRate.PolName,
            PolCode = importRate.PolCode,
            PolSlug = importRate.PolSlug,
            PoeId = importRate.PoeId,
            Poe = importRate.PoeName,
            PoeCode = importRate.PoeCode,
            PoeSlug = importRate.PoeSlug,
            PodId = importRate.PodId,
            Pod = importRate.PodName,
            PodCode = importRate.PodCode,
            PodSlug = importRate.PodSlug,
            CarrierId = importRate.CarrierId,
            Carrier = importRate.CarrierName,
            CarrierCode = importRate.CarrierCode,
            CarrierSlug = importRate.CarrierSlug,
            AgentId = importRate.AgentId,
            Agent = importRate.AgentName,
            AgentCode = importRate.AgentCode,
            AgentSlug = importRate.AgentSlug,
            ContainerTypeId = importRate.ContainerTypeId,
            ContainerType = importRate.ContainerTypeName,
            ContainerTypeCode = importRate.ContainerTypeCode,
            ContainerTypeSlug = importRate.ContainerTypeSlug,
            CurrencyId = importRate.CurrencyId,
            Currency = importRate.CurrencyName,
            CurrencyCode = importRate.CurrencyCode,
            CurrencySlug = importRate.CurrencySlug,
            Commodity = importRate.Commodity,
            SpaceComment = importRate.SpaceComment,
            Freight = importRate.OceanFreight ?? importRate.Freight,
            OceanFreight = importRate.OceanFreight,
            OriginCharges = importRate.OriginCharges,
            DestinationCharges = importRate.DestinationCharges,
            Surcharges = importRate.Surcharges,
            TotalCost = importRate.TotalCost,
            TotalSale = importRate.TotalSale,
            Profit = importRate.Profit,
            Margin = importRate.Margin,
            FreeDays = importRate.FreeDays,
            TransitDays = importRate.TransitDays,
            ValidFrom = importRate.ValidFrom,
            ValidTo = importRate.ValidTo,
            RawDataJson = importRate.RawDataJson ?? "{}",
            Status = importRate.Status.ToString(),
            UsedAsRateCount = importRate.UsedAsRateCount,
            CreatedAsRateHeaderId = importRate.CreatedAsRateHeaderId,
        };
    }

    private static string ResolveShipmentMode(ImportFclRates importRate)
    {
        var containerMarkers = new[]
        {
            importRate.ContainerType,
            importRate.ContainerTypeName,
            importRate.ContainerTypeCode,
            importRate.ContainerTypeSlug,
        };
        var lclMarkers = containerMarkers.Concat(new[]
        {
            importRate.ImportProfileName,
            importRate.ImportProfileCode,
            importRate.ImportProfileSlug,
            importRate.RawDataJson,
        });

        if (lclMarkers.Any(ContainsLclMarker))
            return "Lcl";

        if (containerMarkers.Any(value =>
            string.Equals(value?.Trim(), "AIR", StringComparison.OrdinalIgnoreCase)))
            return "Air";

        return "Fcl";
    }

    private static bool ContainsLclMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("lcl", StringComparison.Ordinal)
            || normalized.Contains("less than container load", StringComparison.Ordinal)
            || normalized.Contains("less-than-container-load", StringComparison.Ordinal);
    }

    public static ImportRateSelectDto ToSelectDto(this ImportFclRates importRate)
    {
        return new ImportRateSelectDto(
            importRate.Id,
            importRate.ImportBatchId,
            importRate.SourceType.ToString(),
            importRate.PolName,
            importRate.PodName,
            importRate.CarrierName,
            importRate.ContainerTypeName,
            importRate.CurrencyName,
            importRate.OceanFreight ?? importRate.Freight,
            importRate.FreeDays,
            importRate.ValidFrom,
            importRate.ValidTo,
            importRate.RawDataJson ?? string.Empty,
            importRate.Status.ToString(),
            importRate.UsedAsRateCount,
            PolId: importRate.PolId,
            PoeId: importRate.PoeId,
            Poe: importRate.PoeName,
            PodId: importRate.PodId,
            CarrierId: importRate.CarrierId,
            ContainerTypeId: importRate.ContainerTypeId,
            ContainerTypeCode: importRate.ContainerTypeCode,
            CurrencyId: importRate.CurrencyId,
            TotalSale: importRate.TotalSale,
            TransitDays: importRate.TransitDays,
            RawSpaceComment: importRate.SpaceComment
        );
    }
}

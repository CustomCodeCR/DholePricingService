namespace Dhole.Pricing.Contracts.Imports.Response;

public sealed record ImportRateDto
{
    public Guid Id { get; init; }
    public Guid ImportBatchId { get; init; }
    public Guid ExtractionRecordId { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public Guid ImportProfileId { get; init; }
    public string ImportProfileName { get; init; } = string.Empty;
    public string ImportProfileCode { get; init; } = string.Empty;
    public string ImportProfileSlug { get; init; } = string.Empty;
    public Guid PolId { get; init; }
    public string Pol { get; init; } = string.Empty;
    public string PolCode { get; init; } = string.Empty;
    public string PolSlug { get; init; } = string.Empty;
    public Guid PoeId { get; init; }
    public string Poe { get; init; } = string.Empty;
    public string PoeCode { get; init; } = string.Empty;
    public string PoeSlug { get; init; } = string.Empty;
    public Guid PodId { get; init; }
    public string Pod { get; init; } = string.Empty;
    public string PodCode { get; init; } = string.Empty;
    public string PodSlug { get; init; } = string.Empty;
    public Guid CarrierId { get; init; }
    public string Carrier { get; init; } = string.Empty;
    public string CarrierCode { get; init; } = string.Empty;
    public string CarrierSlug { get; init; } = string.Empty;
    public Guid AgentId { get; init; }
    public string Agent { get; init; } = string.Empty;
    public string AgentCode { get; init; } = string.Empty;
    public string AgentSlug { get; init; } = string.Empty;
    public Guid ContainerTypeId { get; init; }
    public string ContainerType { get; init; } = string.Empty;
    public string ContainerTypeCode { get; init; } = string.Empty;
    public string ContainerTypeSlug { get; init; } = string.Empty;
    public Guid CurrencyId { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = string.Empty;
    public string CurrencySlug { get; init; } = string.Empty;
    public string? Commodity { get; init; }
    public decimal Freight { get; init; }
    public decimal? OceanFreight { get; init; }
    public decimal? OriginCharges { get; init; }
    public decimal? DestinationCharges { get; init; }
    public decimal? Surcharges { get; init; }
    public decimal? TotalCost { get; init; }
    public decimal? TotalSale { get; init; }
    public decimal? Profit { get; init; }
    public decimal? Margin { get; init; }
    public int FreeDays { get; init; }
    public int? TransitDays { get; init; }
    public DateTime ValidFrom { get; init; }
    public DateTime ValidTo { get; init; }
    public string RawDataJson { get; init; } = "{}";
    public string Status { get; init; } = string.Empty;
    public int UsedAsRateCount { get; init; }
    public Guid? CreatedAsRateHeaderId { get; init; }
}

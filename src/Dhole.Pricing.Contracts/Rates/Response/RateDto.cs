namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateDto(
    Guid Id,
    string RateCode,
    string RateName,
    int RevisionNumber,
    Guid? SourceImportFclRateId,
    Guid? AgentId,
    string? AgentName,
    string? AgentCode,
    Guid? CarrierId,
    string? CarrierName,
    string? CarrierCode,
    Guid PolId,
    string PolName,
    string PolCode,
    Guid PoeId,
    string PoeName,
    string PoeCode,
    Guid? PodId,
    string? PodName,
    string? PodCode,
    Guid ContainerTypeId,
    string ContainerTypeName,
    string ContainerTypeCode,
    Guid? IncotermId,
    string? IncotermName,
    string? IncotermCode,
    string? PickupAddress,
    decimal? PickupLatitude,
    decimal? PickupLongitude,
    int ContainerQuantity,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal? ExchangeRatePurchase,
    decimal? ExchangeRateSale,
    decimal? ExchangeRateApplied,
    DateTime? ExchangeRateDate,
    DateTime? ExchangeRateCapturedAtUtc,
    string? ExchangeRateSource,
    bool ExchangeRateManualOverride,
    int FreeDays,
    DateTime ValidFrom,
    DateTime ValidTo,
    string? ClientName,
    string? ExecutiveName,
    string? IdtraNumber,
    string? QuoNumber,
    string? Includes,
    string? SubjectTo,
    string? Excludes,
    string? TransitTime,
    string RateType,
    string ShipmentMode,
    string OperationType,
    int TotalPackages,
    int TotalPallets,
    decimal TotalWeightKg,
    decimal TotalVolumeCbm,
    decimal KgPerCbm,
    decimal ChargeableQuantity,
    IReadOnlyCollection<RateCargoLineDto> CargoLines,
    decimal TotalCostAmount,
    decimal TotalSaleAmount,
    decimal TotalUtilityAmount,
    decimal TotalCostUsd,
    decimal TotalSaleUsd,
    decimal TotalUtilityUsd,
    decimal TotalCostCrc,
    decimal TotalSaleCrc,
    decimal TotalUtilityCrc,
    decimal MarginPercentage,
    bool RequiredApproval,
    string Status,
    string? ClosedReason,
    DateTime? ClosedAtUtc,
    Guid? ClosedBy,
    IReadOnlyCollection<RateContainerDto> Containers,
    IReadOnlyCollection<RateDetailDto> RateDetails,
    IReadOnlyCollection<RateServiceDto> Services
)
{
    public decimal TotalTaxUsd => CalculateTaxTotals().TaxUsd;
    public decimal TotalTaxCrc => CalculateTaxTotals().TaxCrc;
    public decimal TotalSaleWithTaxUsd => TotalSaleUsd + TotalTaxUsd;
    public decimal TotalSaleWithTaxCrc => TotalSaleCrc + TotalTaxCrc;

    private (decimal TaxUsd, decimal TaxCrc) CalculateTaxTotals()
    {
        decimal taxUsd = 0m;
        decimal taxCrc = 0m;
        var exchangeRate = ExchangeRateApplied is > 0m ? ExchangeRateApplied : ExchangeRateSale;

        foreach (var detail in RateDetails)
        {
            var tax = detail.DestinationTaxAmount;
            if (tax <= 0m) continue;

            var code = detail.CurrencyCode.Trim().ToUpperInvariant();
            if (code == "USD")
            {
                taxUsd += tax;
                if (exchangeRate is > 0m) taxCrc += tax * exchangeRate.Value;
            }
            else if (code == "CRC")
            {
                taxCrc += tax;
                if (exchangeRate is > 0m) taxUsd += tax / exchangeRate.Value;
            }
        }

        return (
            decimal.Round(taxUsd, 2, MidpointRounding.AwayFromZero),
            decimal.Round(taxCrc, 2, MidpointRounding.AwayFromZero)
        );
    }
}

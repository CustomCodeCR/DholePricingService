namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateDetailDto(
    Guid Id,
    Guid RateHeaderId,
    Guid? CostId,
    string Name,
    string CostDetailType,
    string CostType,
    string ChargeBasis,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    decimal UtilityAmount,
    decimal Quantity,
    string? Notes,
    bool ApplyDestinationTax,
    decimal DestinationTaxRate,
    decimal DestinationTaxAmount,
    string? BillToClient
)
{
    // Commercial snapshot produced by Pricing. The UI/PDF must display these
    // figures verbatim; Quantity/SaleAmount remain the original persisted basis.
    public decimal CommercialQuantity { get; init; }
    public decimal CommercialUnitCost { get; init; }
    public decimal CommercialUnitSale { get; init; }
    public decimal CommercialCostSubtotal { get; init; }
    public decimal CommercialSaleSubtotal { get; init; }
    public decimal CommercialTaxAmount { get; init; }
    public decimal CommercialSaleTotal { get; init; }
    public decimal CommercialUtilitySubtotal { get; init; }
    public bool ShowOnClientPdf { get; init; }
}

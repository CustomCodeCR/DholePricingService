using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateDetail : Entity<Guid>
{
    private RateDetail() { }

    private RateDetail(
        Guid id,
        Guid rateHeaderId,
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        ChargeBasis chargeBasis,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        decimal quantity
    )
        : base(id)
    {
        RateHeaderId = rateHeaderId;
        CostId = costId;
        Name = name;
        CostDetailType = costDetailType;
        CostType = costType;
        ChargeBasis = chargeBasis;
        CurrencyId = currencyId;
        CurrencyName = currencyName;
        CurrencyCode = currencyCode;
        CostAmount = costAmount;
        SaleAmount = saleAmount;
        Quantity = quantity;
        UtilityAmount = (saleAmount - costAmount) * quantity;
        Notes = notes;
        SourceType = InferSourceType(costId, costType);
        SourceReference = null;
    }

    public Guid RateHeaderId { get; private set; }
    public Guid? CostId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public CostDetailType CostDetailType { get; private set; }
    public CostType CostType { get; private set; }
    public ChargeBasis ChargeBasis { get; private set; }
    public Guid CurrencyId { get; private set; }
    public string CurrencyName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal CostAmount { get; private set; }
    public decimal SaleAmount { get; private set; }
    public decimal UtilityAmount { get; private set; }
    public string? Notes { get; private set; }
    public decimal Quantity { get; private set; }
    public RateDetailSourceType SourceType { get; private set; } = RateDetailSourceType.Manual;
    public string? SourceReference { get; private set; }
    public bool ApplyDestinationTax { get; private set; }
    public decimal DestinationTaxRate { get; private set; }

    public decimal DestinationTaxAmount =>
        ApplyDestinationTax && DestinationTaxRate > 0m
            ? decimal.Round(SaleAmount * Quantity * DestinationTaxRate / 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

    public void ConfigureDestinationTax(bool applyDestinationTax, decimal destinationTaxRate)
    {
        if (destinationTaxRate < 0m || destinationTaxRate > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationTaxRate));
        }

        ApplyDestinationTax = applyDestinationTax && destinationTaxRate > 0m;
        DestinationTaxRate = ApplyDestinationTax ? destinationTaxRate : 0m;
    }

    public void ConfigureSource(RateDetailSourceType sourceType, string? sourceReference = null)
    {
        SourceType = sourceType;
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
    }

    internal static RateDetail Create(
        Guid rateHeaderId,
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        ChargeBasis chargeBasis,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        decimal quantity
    )
    {
        return new RateDetail(
            Guid.NewGuid(), rateHeaderId, costId, name, costDetailType, costType, chargeBasis,
            currencyId, currencyName, currencyCode, costAmount, saleAmount, notes, quantity
        );
    }

    internal void SetQuantity(decimal quantity)
    {
        if (quantity <= 0m)
            throw new InvalidOperationException("La cantidad del detalle debe ser mayor que cero.");

        Quantity = quantity;
        UtilityAmount = (SaleAmount - CostAmount) * quantity;
    }

    internal void Update(
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        ChargeBasis chargeBasis,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        decimal quantity
    )
    {
        CostId = costId;
        Name = name;
        CostDetailType = costDetailType;
        CostType = costType;
        ChargeBasis = chargeBasis;
        CurrencyId = currencyId;
        CurrencyName = currencyName;
        CurrencyCode = currencyCode;
        CostAmount = costAmount;
        SaleAmount = saleAmount;
        Quantity = quantity;
        UtilityAmount = (saleAmount - costAmount) * quantity;
        Notes = notes;
        SourceType = InferSourceType(costId, costType);
        if (SourceType == RateDetailSourceType.CostCatalog) SourceReference = null;
    }

    private static RateDetailSourceType InferSourceType(Guid? costId, CostType costType)
    {
        if (costId.HasValue) return RateDetailSourceType.CostCatalog;
        return costType == CostType.Fixed
            ? RateDetailSourceType.ExternalSnapshot
            : RateDetailSourceType.Manual;
    }
}

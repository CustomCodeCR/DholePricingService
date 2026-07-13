using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Costs.Enums;

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
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes
    )
        : base(id)
    {
        RateHeaderId = rateHeaderId;
        CostId = costId;
        Name = name;
        CostDetailType = costDetailType;
        CostType = costType;
        CurrencyId = currencyId;
        CurrencyName = currencyName;
        CurrencyCode = currencyCode;
        CostAmount = costAmount;
        SaleAmount = saleAmount;
        UtilityAmount = saleAmount - costAmount;

        Notes = notes;
    }

    public Guid RateHeaderId { get; private set; }

    public Guid? CostId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public CostDetailType CostDetailType { get; private set; }

    public CostType CostType { get; private set; }

    public Guid CurrencyId { get; private set; }
    public string CurrencyName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;

    public decimal CostAmount { get; private set; }
    public decimal SaleAmount { get; private set; }
    public decimal UtilityAmount { get; private set; }

    public string? Notes { get; private set; }

    internal static RateDetail Create(
        Guid rateHeaderId,
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes
    )
    {
        return new RateDetail(
            Guid.NewGuid(),
            rateHeaderId,
            costId,
            name,
            costDetailType,
            costType,
            currencyId,
            currencyName,
            currencyCode,
            costAmount,
            saleAmount,
            notes
        );
    }

    internal void Update(
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes
    )
    {
        CostId = costId;
        Name = name;
        CostDetailType = costDetailType;
        CostType = costType;
        CurrencyId = currencyId;
        CurrencyName = currencyName;
        CurrencyCode = currencyCode;
        CostAmount = costAmount;
        SaleAmount = saleAmount;
        UtilityAmount = saleAmount - costAmount;

        Notes = notes;
    }
}

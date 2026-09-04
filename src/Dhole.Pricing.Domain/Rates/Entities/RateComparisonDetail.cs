using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateComparisonDetail : Entity<Guid>
{
    private RateComparisonDetail() { }

    private RateComparisonDetail(
        Guid id,
        Guid rateComparisonId,
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        ChargeBasis chargeBasis,
        string currencyCode,
        decimal quantity,
        decimal baselineCostAmount,
        decimal baselineSaleAmount,
        decimal candidateCostAmount,
        decimal candidateSaleAmount,
        string? notes
    ) : base(id)
    {
        RateComparisonId = rateComparisonId;
        CostId = costId;
        Name = name.Trim();
        CostDetailType = costDetailType;
        CostType = costType;
        ChargeBasis = chargeBasis;
        CurrencyCode = currencyCode.Trim();
        Quantity = quantity;
        BaselineCostAmount = baselineCostAmount;
        BaselineSaleAmount = baselineSaleAmount;
        CandidateCostAmount = candidateCostAmount;
        CandidateSaleAmount = candidateSaleAmount;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public Guid RateComparisonId { get; private set; }
    public Guid? CostId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public CostDetailType CostDetailType { get; private set; }
    public CostType CostType { get; private set; }
    public ChargeBasis ChargeBasis { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal BaselineCostAmount { get; private set; }
    public decimal BaselineSaleAmount { get; private set; }
    public decimal CandidateCostAmount { get; private set; }
    public decimal CandidateSaleAmount { get; private set; }
    public string? Notes { get; private set; }

    public static RateComparisonDetail Create(
        Guid rateComparisonId,
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        ChargeBasis chargeBasis,
        string currencyCode,
        decimal quantity,
        decimal baselineCostAmount,
        decimal baselineSaleAmount,
        decimal candidateCostAmount,
        decimal candidateSaleAmount,
        string? notes)
        => new(
            Guid.NewGuid(), rateComparisonId, costId, name, costDetailType, costType,
            chargeBasis, currencyCode, quantity, baselineCostAmount, baselineSaleAmount,
            candidateCostAmount, candidateSaleAmount, notes);
}

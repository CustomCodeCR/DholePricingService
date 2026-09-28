using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;

namespace Dhole.Pricing.Domain.MarketPricing.Entities;

public sealed class ChargePricingRule : Entity<Guid>
{
    private ChargePricingRule() { }

    private ChargePricingRule(
        Guid id,
        string chargeCode,
        bool canAutoAdjust,
        int adjustmentPriority,
        decimal minimumMarkup,
        decimal? maximumMarkup,
        decimal? maximumAdjustmentAmount,
        ChargeAdjustmentStrategy adjustmentStrategy,
        bool isActive
    ) : base(id)
    {
        Apply(
            chargeCode,
            canAutoAdjust,
            adjustmentPriority,
            minimumMarkup,
            maximumMarkup,
            maximumAdjustmentAmount,
            adjustmentStrategy,
            isActive
        );
    }

    public string ChargeCode { get; private set; } = string.Empty;
    public bool CanAutoAdjust { get; private set; }
    public int AdjustmentPriority { get; private set; }
    public decimal MinimumMarkup { get; private set; }
    public decimal? MaximumMarkup { get; private set; }
    public decimal? MaximumAdjustmentAmount { get; private set; }
    public ChargeAdjustmentStrategy AdjustmentStrategy { get; private set; }
    public bool IsActive { get; private set; }

    public static ChargePricingRule Create(
        string chargeCode,
        bool canAutoAdjust,
        int adjustmentPriority,
        decimal minimumMarkup,
        decimal? maximumMarkup,
        decimal? maximumAdjustmentAmount,
        ChargeAdjustmentStrategy adjustmentStrategy,
        bool isActive = true
    ) => new(
        Guid.NewGuid(),
        chargeCode,
        canAutoAdjust,
        adjustmentPriority,
        minimumMarkup,
        maximumMarkup,
        maximumAdjustmentAmount,
        adjustmentStrategy,
        isActive
    );

    public void Update(
        bool canAutoAdjust,
        int adjustmentPriority,
        decimal minimumMarkup,
        decimal? maximumMarkup,
        decimal? maximumAdjustmentAmount,
        ChargeAdjustmentStrategy adjustmentStrategy,
        bool isActive
    ) => Apply(
        ChargeCode,
        canAutoAdjust,
        adjustmentPriority,
        minimumMarkup,
        maximumMarkup,
        maximumAdjustmentAmount,
        adjustmentStrategy,
        isActive
    );

    private void Apply(
        string chargeCode,
        bool canAutoAdjust,
        int adjustmentPriority,
        decimal minimumMarkup,
        decimal? maximumMarkup,
        decimal? maximumAdjustmentAmount,
        ChargeAdjustmentStrategy adjustmentStrategy,
        bool isActive
    )
    {
        if (string.IsNullOrWhiteSpace(chargeCode))
        {
            throw new InvalidOperationException("El código del rubro es obligatorio.");
        }

        if (adjustmentPriority < 0)
        {
            throw new InvalidOperationException("La prioridad de ajuste no puede ser negativa.");
        }

        if (
            minimumMarkup < 0m
            || (maximumMarkup.HasValue && maximumMarkup.Value < 0m)
            || (maximumAdjustmentAmount.HasValue && maximumAdjustmentAmount.Value < 0m)
        )
        {
            throw new InvalidOperationException("Los límites de ajuste no pueden ser negativos.");
        }

        if (maximumMarkup.HasValue && maximumMarkup.Value < minimumMarkup)
        {
            throw new InvalidOperationException("El markup máximo no puede ser menor que el markup mínimo.");
        }

        if (!Enum.IsDefined(adjustmentStrategy))
        {
            throw new InvalidOperationException("La estrategia de ajuste no es válida.");
        }

        ChargeCode = chargeCode.Trim().ToUpperInvariant();
        CanAutoAdjust = canAutoAdjust;
        AdjustmentPriority = adjustmentPriority;
        MinimumMarkup = minimumMarkup;
        MaximumMarkup = maximumMarkup;
        MaximumAdjustmentAmount = maximumAdjustmentAmount;
        AdjustmentStrategy = adjustmentStrategy;
        IsActive = isActive;
    }
}

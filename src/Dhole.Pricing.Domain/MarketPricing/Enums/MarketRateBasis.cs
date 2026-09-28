namespace Dhole.Pricing.Domain.MarketPricing.Enums;

public enum MarketRateBasis
{
    Unknown = 0,
    PerContainer = 1,
    PerShipment = 2,
    PerTeu = 3,
    PerKg = 4,
    PerCbm = 5,
    WeightOrMeasure = 6,
    PerHbl = 7,
    PerTruck = 8,
    Per100Kg = 9,
    PerTon = 10,
    Minimum = 11,
}

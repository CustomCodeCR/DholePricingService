namespace Dhole.Pricing.Domain.Costs.Enums;

public enum ChargeBasis
{
    PerShipment = 0,
    PerContainer = 1,
    PerTruck = 2,
    PerTeu = 3,
    PerCbm = 10,
    PerChargeableCbm = 11,
    PerKg = 20,
    Per100Kg = 21,
    PerTon = 22,
    PerPallet = 30,
    PerPackage = 31,
    PerDocument = 40,
    PerService = 50,
}

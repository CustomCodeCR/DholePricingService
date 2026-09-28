using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.MarketPricing.Models;

public sealed record MarketComparisonKey(
    Guid? IncotermId,
    Guid PolId,
    Guid? PoeId,
    Guid? PodId,
    Guid? ContainerTypeId,
    ShipmentMode Mode,
    Guid? CarrierId
);

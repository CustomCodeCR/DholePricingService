using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.Pricing.Domain.Rates.Events;

public sealed record RateHeaderAmountsChangedDomainEvent(
    Guid id,
    decimal costs,
    decimal sales,
    decimal utitlies,
    decimal margin,
    Guid? updatedBy
) : DomainEvent;

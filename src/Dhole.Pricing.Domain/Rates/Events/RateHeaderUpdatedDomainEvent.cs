using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.Pricing.Domain.Rates.Events;

public sealed record RateHeaderUpdatedDomainEvent(Guid id, Guid? updatedBy) : DomainEvent;

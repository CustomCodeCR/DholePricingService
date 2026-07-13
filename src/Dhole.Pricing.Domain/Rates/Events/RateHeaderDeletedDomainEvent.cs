using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.Pricing.Domain.Rates.Events;

public sealed record RateHeaderDeletedDomainEvent(Guid id, Guid? deletedBy) : DomainEvent;

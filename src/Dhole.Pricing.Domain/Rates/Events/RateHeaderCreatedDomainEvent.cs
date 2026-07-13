using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.Pricing.Domain.Rates.Events;

public sealed record RateHeaderCreatedDomainEvent(Guid id, Guid? createdBy) : DomainEvent;

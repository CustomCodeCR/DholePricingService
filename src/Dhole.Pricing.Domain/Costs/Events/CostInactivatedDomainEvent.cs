using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.Pricing.Domain.Costs.Events;

public sealed record CostInactivatedDomainEvent(Guid id, string name, Guid? updatedBy)
    : DomainEvent;

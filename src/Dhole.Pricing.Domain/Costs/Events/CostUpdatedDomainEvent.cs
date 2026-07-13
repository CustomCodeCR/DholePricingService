using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.Pricing.Domain.Costs.Events;

public sealed record CostUpdatedDomainEvent(Guid id, string name, Guid? updatedBy) : DomainEvent;

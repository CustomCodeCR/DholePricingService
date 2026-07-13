using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.Pricing.Domain.Costs.Events;

public sealed record CostCreatedDomainEvent(Guid id, string name, Guid? createdBy) : DomainEvent;

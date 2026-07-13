using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.Pricing.Domain.Costs.Events;

public sealed record CostDeletedDomainEvent(Guid id, string name, Guid? deletedBy) : DomainEvent;

using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.Pricing.Domain.Imports.Events;

public sealed record ImportFclRateDeletedDomainEvent(Guid id, Guid? deletedBy) : DomainEvent;

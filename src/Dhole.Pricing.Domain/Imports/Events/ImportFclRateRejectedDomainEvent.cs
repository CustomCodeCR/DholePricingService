using CustomCodeFramework.Core.Domain.Events;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Domain.Imports.Events;

public sealed record ImportFclRateRejectDomainEvent(Guid id, ImportStatus status, Guid? updatedBy)
    : DomainEvent;

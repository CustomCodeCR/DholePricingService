using CustomCodeFramework.Core.Domain.Events;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Domain.Imports.Events;

public sealed record ImportFclRateCreatedAsRateDomainEvent(
    Guid id,
    Guid rateHeaderId,
    ImportStatus status,
    Guid? updatedBy
) : DomainEvent;

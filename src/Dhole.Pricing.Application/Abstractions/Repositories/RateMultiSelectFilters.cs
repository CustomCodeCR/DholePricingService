using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Abstractions.Repositories;

/// <summary>Multiple selections are ORed within a category and ANDed across categories.</summary>
public sealed record RateMultiSelectFilters(
    IReadOnlyCollection<RateStatus> Statuses,
    IReadOnlyCollection<Guid> AgentIds,
    IReadOnlyCollection<Guid> CarrierIds,
    IReadOnlyCollection<Guid> PolIds,
    IReadOnlyCollection<Guid> PoeIds,
    IReadOnlyCollection<Guid> PodIds,
    IReadOnlyCollection<Guid> ContainerTypeIds,
    IReadOnlyCollection<Guid> CurrencyIds,
    bool IncludePendingReapproval = false
);

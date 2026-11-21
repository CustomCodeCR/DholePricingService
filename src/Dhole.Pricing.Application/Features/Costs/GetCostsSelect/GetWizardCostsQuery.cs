using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Costs.GetCostsForSelect;

/// <summary>Request-scoped context for loading all P7 costs in a single HTTP response.</summary>
public sealed record GetWizardCostsQuery(
    Guid? CarrierId,
    Guid? AgentId,
    Guid? PolId,
    Guid? PoeId,
    Guid? PodId,
    Guid? IncotermId,
    ShipmentMode? ShipmentMode,
    IReadOnlyCollection<Guid>? ServiceIds,
    Guid? ImportRateId = null,
    decimal? WeightKg = null,
    int? Packages = null,
    Guid? ContainerTypeId = null,
    int? StorageDays = null
) : IQuery<Result<CostWizardBundleDto>>;

using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Imports.ReviewImportRate;

public sealed record ReviewImportRateCommand(
    Guid ImportRateId,
    Guid ImportProfileId,
    Guid PolId,
    Guid PoeId,
    Guid PodId,
    Guid CarrierId,
    Guid AgentId,
    Guid ContainerTypeId,
    Guid CurrencyId,
    string? Commodity,
    string? SpaceComment,
    decimal OceanFreight,
    decimal OriginCharges,
    decimal DestinationCharges,
    decimal Surcharges,
    decimal? TotalSale,
    int FreeDays,
    int? TransitDays,
    DateTime ValidFrom,
    DateTime ValidTo,
    string? ReviewNotes,
    Guid? UpdatedBy
) : ICommand<Result>;

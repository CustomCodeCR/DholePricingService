using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Features.Imports.CreateImportRate;

public sealed record CreateImportRateCommand(
    Guid ImportBatchId,
    Guid ExtractionRecordId,
    ImportSourceType SourceType,
    CatalogSnapshot Profile,
    CatalogSnapshot Pol,
    CatalogSnapshot Poe,
    CatalogSnapshot Pod,
    CatalogSnapshot Carrier,
    CatalogSnapshot Agent,
    CatalogSnapshot ContainerType,
    CatalogSnapshot Currency,
    string? Commodity,
    decimal? OceanFreight,
    decimal? OriginCharges,
    decimal? DestinationCharges,
    decimal? Surcharges,
    decimal? TotalCost,
    decimal? TotalSale,
    decimal? Profit,
    decimal? Margin,
    int FreeDays,
    int? TransitDays,
    DateTime ValidFrom,
    DateTime ValidTo,
    string? RawDataJson,
    Guid? CreatedBy
) : ICommand<Result<Guid>>;

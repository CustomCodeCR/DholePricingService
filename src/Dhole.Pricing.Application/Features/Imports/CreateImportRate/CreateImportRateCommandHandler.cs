using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Imports.CreateImportRate;

public sealed class CreateImportRateCommandHandler(
    IImportFclRateRepository importRates,
    IPricingConfigCatalogClient configCatalog,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreateImportRateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateImportRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        ImportFclRates importRate;

        try
        {
            var profile = await ResolveSnapshotAsync(
                command.Profile,
                "pricing-imports-profiles",
                cancellationToken
            );
            var pol = await ResolveSnapshotAsync(command.Pol, "pol", cancellationToken);
            var poe = await ResolveSnapshotAsync(command.Poe, "poe", cancellationToken);
            var pod = await ResolveSnapshotAsync(command.Pod, "pod", cancellationToken);
            var carrier = await ResolveSnapshotAsync(
                command.Carrier,
                "carriers",
                cancellationToken
            );
            var agent = await ResolveSnapshotAsync(command.Agent, "agents", cancellationToken);
            var containerType = await ResolveSnapshotAsync(
                command.ContainerType,
                "container-types",
                cancellationToken
            );
            var currency = await ResolveSnapshotAsync(
                command.Currency,
                "currencies",
                cancellationToken
            );

            if (
                profile is null
                || pol is null
                || poe is null
                || pod is null
                || carrier is null
                || agent is null
                || containerType is null
                || currency is null
            )
            {
                return Result.Failure<Guid>(
                    PricingErrors.ImportFclRateCatalogConcordanceRequired
                );
            }

            importRate = ImportFclRates.Create(
                command.ImportBatchId,
                command.ExtractionRecordId,
                command.SourceType,
                profile,
                pol,
                poe,
                pod,
                carrier,
                agent,
                containerType,
                currency,
                command.Commodity,
                command.OceanFreight,
                command.OriginCharges,
                command.DestinationCharges,
                command.Surcharges,
                command.TotalCost,
                command.TotalSale,
                command.Profit,
                command.Margin,
                command.FreeDays,
                command.TransitDays,
                command.ValidFrom,
                command.ValidTo,
                command.RawDataJson,
                command.CreatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.InvalidImportFclRate);
        }

        await importRates.AddAsync(importRate, cancellationToken);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.ImportFclRateCreated,
                Action: PricingAuditActions.Created,
                EntityType: PricingAuditEntityTypes.ImportFclRate,
                EntityId: importRate.Id,
                ActorUserId: command.CreatedBy,
                After: PricingAuditSnapshots.From(importRate),
                Payload: PricingAuditSnapshots.From(importRate)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveImportRateCacheAsync(
            importRate.Id,
            importRate.ImportBatchId,
            cancellationToken
        );

        return Result.Success(importRate.Id);
    }

    private async Task<CatalogSnapshot?> ResolveSnapshotAsync(
        CatalogSnapshot requested,
        string expectedGroupSlug,
        CancellationToken cancellationToken
    )
    {
        var item = await configCatalog.GetActiveByIdAsync(requested.Id, cancellationToken);
        if (
            item is null
            || !item.CatalogGroupSlug.Equals(
                expectedGroupSlug,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        return CatalogSnapshot.Create(item.Id, item.Name, item.Code, item.Slug);
    }
}

using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Imports.ReviewImportRate;

public sealed class ReviewImportRateCommandHandler(
    IImportFclRateRepository importRates,
    IPricingConfigCatalogClient configCatalog,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<ReviewImportRateCommand, Result>
{
    public async Task<Result> HandleAsync(
        ReviewImportRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var importRate = await importRates.GetByIdAsync(command.ImportRateId, cancellationToken);
        if (importRate is null || importRate.IsDeleted)
        {
            return Result.Failure(PricingErrors.ImportFclRateNotFound);
        }

        if (importRate.Status != ImportStatus.Pending)
        {
            return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
        }

        if (
            command.OceanFreight < 0m
            || command.OriginCharges < 0m
            || command.DestinationCharges < 0m
            || command.Surcharges < 0m
            || command.TotalSale < 0m
            || command.FreeDays < 0
            || command.TransitDays < 0
            || command.ValidTo < command.ValidFrom
        )
        {
            return Result.Failure(PricingErrors.InvalidImportFclRate);
        }

        var profile = await ResolveAsync(command.ImportProfileId, ["pricing-imports-profiles"], cancellationToken);
        var pol = await ResolveAsync(command.PolId, ["pol", "ports"], cancellationToken);
        var poe = await ResolveAsync(command.PoeId, ["poe", "ports"], cancellationToken);
        var pod = await ResolveAsync(command.PodId, ["pod", "ports"], cancellationToken);
        var carrier = await ResolveAsync(command.CarrierId, ["carriers"], cancellationToken);
        var agent = await ResolveAsync(command.AgentId, ["agents"], cancellationToken);
        var containerType = await ResolveAsync(command.ContainerTypeId, ["container-types", "containers-types"], cancellationToken);
        var currency = await ResolveAsync(command.CurrencyId, ["currencies"], cancellationToken);

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
            return Result.Failure(PricingErrors.ImportFclRateCatalogConcordanceRequired);
        }

        var before = PricingAuditSnapshots.From(importRate);

        try
        {
            importRate.ApplyManualReview(
                Snapshot(profile),
                Snapshot(pol),
                Snapshot(poe),
                Snapshot(pod),
                Snapshot(carrier),
                Snapshot(agent),
                Snapshot(containerType),
                Snapshot(currency),
                command.Commodity,
                command.SpaceComment,
                command.OceanFreight,
                command.OriginCharges,
                command.DestinationCharges,
                command.Surcharges,
                command.TotalSale,
                command.FreeDays,
                command.TransitDays,
                command.ValidFrom,
                command.ValidTo,
                command.UpdatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(PricingErrors.InvalidImportFclRate);
        }

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.ImportFclRateUpdated,
                Action: PricingAuditActions.Updated,
                EntityType: PricingAuditEntityTypes.ImportFclRate,
                EntityId: importRate.Id,
                ActorUserId: command.UpdatedBy,
                Before: before,
                After: PricingAuditSnapshots.From(importRate),
                Payload: new
                {
                    importRate.Id,
                    importRate.ImportBatchId,
                    ReviewApplied = true,
                    command.ReviewNotes,
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveImportRateCacheAsync(
            importRate.Id,
            importRate.ImportBatchId,
            cancellationToken
        );

        return Result.Success();
    }

    private async Task<PricingConfigCatalogItem?> ResolveAsync(
        Guid id,
        IReadOnlyCollection<string> acceptedGroups,
        CancellationToken cancellationToken
    )
    {
        if (id == Guid.Empty) return null;

        var item = await configCatalog.GetActiveByIdAsync(id, cancellationToken);
        return item is not null
            && acceptedGroups.Contains(item.CatalogGroupSlug, StringComparer.OrdinalIgnoreCase)
            ? item
            : null;
    }

    private static CatalogSnapshot Snapshot(PricingConfigCatalogItem item) =>
        CatalogSnapshot.Create(item.Id, item.Name, item.Code, item.Slug);
}

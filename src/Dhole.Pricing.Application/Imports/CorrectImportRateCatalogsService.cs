using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Imports;

public sealed class CorrectImportRateCatalogsService(
    IImportFclRateRepository importRates,
    IPricingConfigCatalogClient configCatalog,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
)
{
    public async Task<Result> CorrectAsync(
        Guid importRateId,
        ImportRateCatalogCorrection correction,
        Guid? updatedBy,
        CancellationToken cancellationToken = default
    )
    {
        var importRate = await importRates.GetByIdAsync(importRateId, cancellationToken);
        if (importRate is null || importRate.IsDeleted)
        {
            return Result.Failure(PricingErrors.ImportFclRateNotFound);
        }

        if (importRate.Status != ImportStatus.Pending)
        {
            return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
        }

        var profile = await ResolveAsync(
            correction.ImportProfileId,
            "pricing-imports-profiles",
            cancellationToken
        );
        var pol = await ResolveAsync(correction.PolId, "pol", cancellationToken);
        var poe = await ResolveAsync(correction.PoeId, "poe", cancellationToken);
        var pod = await ResolveAsync(correction.PodId, "pod", cancellationToken);
        var carrier = await ResolveAsync(
            correction.CarrierId,
            "carriers",
            cancellationToken
        );
        var agent = await ResolveAsync(correction.AgentId, "agents", cancellationToken);
        var containerType = await ResolveAsync(
            correction.ContainerTypeId,
            "container-types",
            cancellationToken
        );
        var currency = await ResolveAsync(
            correction.CurrencyId,
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
            return Result.Failure(
                PricingErrors.ImportFclRateCatalogConcordanceRequired
            );
        }

        var before = PricingAuditSnapshots.From(importRate);

        importRate.CorrectCatalogReferences(
            profile,
            pol,
            poe,
            pod,
            carrier,
            agent,
            containerType,
            currency,
            updatedBy
        );

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.ImportFclRateUpdated,
                Action: PricingAuditActions.Updated,
                EntityType: PricingAuditEntityTypes.ImportFclRate,
                EntityId: importRate.Id,
                ActorUserId: updatedBy,
                Before: before,
                After: PricingAuditSnapshots.From(importRate)
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

    private async Task<CatalogSnapshot?> ResolveAsync(
        Guid id,
        string expectedGroupSlug,
        CancellationToken cancellationToken
    )
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        var item = await configCatalog.GetActiveByIdAsync(id, cancellationToken);
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

public sealed record ImportRateCatalogCorrection(
    Guid ImportProfileId,
    Guid PolId,
    Guid PoeId,
    Guid PodId,
    Guid CarrierId,
    Guid AgentId,
    Guid ContainerTypeId,
    Guid CurrencyId
);

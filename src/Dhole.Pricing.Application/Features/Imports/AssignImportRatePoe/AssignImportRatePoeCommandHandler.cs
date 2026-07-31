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

namespace Dhole.Pricing.Application.Features.Imports.AssignImportRatePoe;

public sealed class AssignImportRatePoeCommandHandler(
    IImportFclRateRepository importRates,
    IPricingConfigCatalogClient configCatalog,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<AssignImportRatePoeCommand, Result>
{
    public async Task<Result> HandleAsync(
        AssignImportRatePoeCommand command,
        CancellationToken cancellationToken = default
    )
    {
        if (command.ImportRateId == Guid.Empty || command.PoeId == Guid.Empty)
        {
            return Result.Failure(PricingErrors.ImportFclRatePoeAssignmentRequired);
        }

        var importRate = await importRates.GetByIdAsync(command.ImportRateId, cancellationToken);
        if (importRate is null || importRate.IsDeleted)
        {
            return Result.Failure(PricingErrors.ImportFclRateNotFound);
        }

        if (importRate.Status != ImportStatus.Pending)
        {
            return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
        }

        var poe = await configCatalog.GetActiveByIdAsync(command.PoeId, cancellationToken);
        if (
            poe is null
            || !poe.CatalogGroupSlug.Equals("poe", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Result.Failure(PricingErrors.ImportFclRatePoeAssignmentRequired);
        }

        var before = PricingAuditSnapshots.From(importRate);

        importRate.AssignPoe(
            CatalogSnapshot.Create(poe.Id, poe.Name, poe.Code, poe.Slug),
            command.UpdatedBy
        );

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
                    PoeId = poe.Id,
                    PoeName = poe.Name,
                    PoeCode = poe.Code,
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
}

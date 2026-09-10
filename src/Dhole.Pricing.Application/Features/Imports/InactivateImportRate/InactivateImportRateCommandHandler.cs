using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Imports.InactivateImportRate;

public sealed class InactivateImportRateCommandHandler(
    IImportFclRateRepository importRates,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<InactivateImportRateCommand, Result>
{
    public async Task<Result> HandleAsync(
        InactivateImportRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        if (command.ImportRateId == Guid.Empty)
        {
            return Result.Failure(PricingErrors.InvalidImportFclRate);
        }

        var importRate = await importRates.GetByIdAsync(command.ImportRateId, cancellationToken);

        if (importRate is null || importRate.IsDeleted)
        {
            return Result.Failure(PricingErrors.ImportFclRateNotFound);
        }

        if (importRate.Status == ImportStatus.Inactive)
        {
            return Result.Success();
        }

        importRate.ExpireIfNeeded(DateTime.UtcNow.Date, command.InactivatedBy);

        if (!importRate.CanBeInactivated)
        {
            return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
        }

        var before = PricingAuditSnapshots.From(importRate);

        importRate.Inactivate(command.InactivatedBy);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.ImportFclRateUpdated,
                Action: PricingAuditActions.Inactivated,
                EntityType: PricingAuditEntityTypes.ImportFclRate,
                EntityId: importRate.Id,
                ActorUserId: command.InactivatedBy,
                Before: before,
                After: PricingAuditSnapshots.From(importRate),
                Payload: new
                {
                    importRate.Id,
                    importRate.ImportBatchId,
                    Status = importRate.Status.ToString(),
                    importRate.UsedAsRateCount,
                    importRate.CreatedAsRateHeaderId,
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

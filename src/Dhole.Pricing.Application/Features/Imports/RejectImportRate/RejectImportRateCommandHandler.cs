using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Imports.RejectImportRate;

public sealed class RejectImportRateCommandHandler(
    IImportFclRateRepository importRates,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<RejectImportRateCommand, Result>
{
    public async Task<Result> HandleAsync(
        RejectImportRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var importRate = await importRates.GetByIdAsync(command.Id, cancellationToken);

        if (importRate is null || importRate.IsDeleted)
        {
            return Result.Failure(PricingErrors.ImportFclRateNotFound);
        }

        if (importRate.Status == ImportStatus.Rejected)
        {
            return Result.Success();
        }

        if (importRate.Status != ImportStatus.Pending)
        {
            return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
        }

        var reason = command.Reason.Trim();
        var before = PricingAuditSnapshots.From(importRate);

        importRate.Reject(command.RejectedBy);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.ImportFclRateRejected,
                Action: PricingAuditActions.Rejected,
                EntityType: PricingAuditEntityTypes.ImportFclRate,
                EntityId: importRate.Id,
                ActorUserId: command.RejectedBy,
                Before: before,
                After: PricingAuditSnapshots.From(importRate),
                Payload: new
                {
                    importRate.Id,
                    importRate.ImportBatchId,
                    Reason = reason,
                    Status = importRate.Status.ToString(),
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

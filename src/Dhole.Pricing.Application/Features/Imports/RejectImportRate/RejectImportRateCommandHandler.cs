using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Imports.Entities;
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
        var ids = command.Ids.Where(x => x != Guid.Empty).Distinct().ToArray();

        if (ids.Length == 0)
            return Result.Failure(PricingErrors.InvalidImportFclRate);

        var entities = new List<ImportFclRates>(ids.Length);

        foreach (var id in ids)
        {
            var importRate = await importRates.GetByIdAsync(id, cancellationToken);

            if (importRate is null || importRate.IsDeleted)
                return Result.Failure(PricingErrors.ImportFclRateNotFound);
        }

        foreach (var importRate in entities)
        {
            var reason = command.Reason;
            var before = PricingAuditSnapshots.From(importRate);

            if (importRate.Status == ImportStatus.Rejected)
                return Result.Success();

            if (importRate.Status != ImportStatus.Pending)
                return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);

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
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var importRate in entities)
        {
            await cache.RemoveImportRateCacheAsync(
                importRate.Id,
                importRate.ImportBatchId,
                cancellationToken
            );
        }

        return Result.Success();
    }
}

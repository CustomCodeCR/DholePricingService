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
        var reason = command.Reason?.Trim();

        if (ids.Length == 0)
        {
            return Result.Failure(PricingErrors.InvalidImportFclRate);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(PricingErrors.ImportFclRateRejectReasonIsRequired);
        }

        var entities = new List<ImportFclRates>(ids.Length);

        foreach (var id in ids)
        {
            var importRate = await importRates.GetByIdAsync(id, cancellationToken);

            if (importRate is null || importRate.IsDeleted)
            {
                return Result.Failure(PricingErrors.ImportFclRateNotFound);
            }

            if (importRate.Status is not (ImportStatus.Pending or ImportStatus.Rejected))
            {
                return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
            }

            entities.Add(importRate);
        }

        var pendingEntities = entities
            .Where(importRate => importRate.Status == ImportStatus.Pending)
            .ToArray();

        foreach (var importRate in pendingEntities)
        {
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
        }

        if (pendingEntities.Length == 0)
        {
            return Result.Success();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var importRate in pendingEntities)
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

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

namespace Dhole.Pricing.Application.Features.Imports.ApproveImportRate;

public sealed class ApproveImportRateCommandHandler(
    IImportFclRateRepository importRates,
    IPricingConfigCatalogClient configCatalog,
    IImportRateAiFeedbackStore aiFeedback,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<ApproveImportRateCommand, Result>
{
    public async Task<Result> HandleAsync(
        ApproveImportRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var ids = command.Ids.Where(x => x != Guid.Empty).Distinct().ToArray();

        if (ids.Length == 0)
        {
            return Result.Failure(PricingErrors.InvalidImportFclRate);
        }

        // Toda tarifa originada por Email/PDF/Excel/CSV/Imagen debe pasar primero por
        // revisión humana explícita. Las importaciones manuales no requieren feedback de IA.
        // El store únicamente considera como revisadas las decisiones verified/corrected;
        // una fila marcada rejected nunca puede aprobarse por un flujo batch accidental.
        var missingHumanReview = await aiFeedback.GetMissingReviewIdsAsync(ids, cancellationToken);
        if (missingHumanReview.Count > 0)
        {
            return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
        }

        var entities = new List<ImportFclRates>(ids.Length);

        foreach (var id in ids)
        {
            var importRate = await importRates.GetByIdAsync(id, cancellationToken);

            if (importRate is null || importRate.IsDeleted)
            {
                return Result.Failure(PricingErrors.ImportFclRateNotFound);
            }

            importRate.ExpireIfNeeded(DateTime.UtcNow.Date, command.ApprovedBy);

            if (importRate.Status == ImportStatus.Expired)
            {
                return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
            }

            if (importRate.Status is not (ImportStatus.Pending or ImportStatus.PreAuthorized or ImportStatus.Approved))
            {
                return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
            }

            var assignedPoe = await configCatalog.GetActiveByIdAsync(
                importRate.PoeId,
                cancellationToken
            );
            if (
                assignedPoe is null
                || !assignedPoe.CatalogGroupSlug.Equals(
                    "poe",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Result.Failure(PricingErrors.ImportFclRatePoeAssignmentRequired);
            }

            entities.Add(importRate);
        }

        var pendingEntities = entities
            .Where(importRate => importRate.Status is ImportStatus.Pending or ImportStatus.PreAuthorized)
            .ToArray();

        foreach (var importRate in pendingEntities)
        {
            var before = PricingAuditSnapshots.From(importRate);

            importRate.Approve(command.ApprovedBy);

            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.ImportFclRateApproved,
                    Action: PricingAuditActions.Approved,
                    EntityType: PricingAuditEntityTypes.ImportFclRate,
                    EntityId: importRate.Id,
                    ActorUserId: command.ApprovedBy,
                    Before: before,
                    After: PricingAuditSnapshots.From(importRate),
                    Payload: new
                    {
                        importRate.Id,
                        importRate.ImportBatchId,
                        Status = importRate.Status.ToString(),
                        HumanAiReviewRequired = true,
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

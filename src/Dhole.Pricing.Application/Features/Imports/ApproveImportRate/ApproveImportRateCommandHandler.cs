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

            if (importRate.Status is not (ImportStatus.Pending or ImportStatus.Approved))
            {
                return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
            }

            if (!importRate.HasConfigConcordance)
            {
                return Result.Failure(
                    PricingErrors.ImportFclRateCatalogConcordanceRequired
                );
            }

            if (
                importRate.Status == ImportStatus.Pending
                && !await RefreshCatalogsFromConfigAsync(
                    importRate,
                    command.ApprovedBy,
                    cancellationToken
                )
            )
            {
                return Result.Failure(
                    PricingErrors.ImportFclRateCatalogConcordanceRequired
                );
            }

            entities.Add(importRate);
        }

        var pendingEntities = entities
            .Where(importRate => importRate.Status == ImportStatus.Pending)
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

    private async Task<bool> RefreshCatalogsFromConfigAsync(
        ImportFclRates importRate,
        Guid? updatedBy,
        CancellationToken cancellationToken
    )
    {
        var profile = ResolveAsync(
            importRate.ImportProfileId,
            "pricing-imports-profiles",
            cancellationToken
        );
        var pol = ResolveAsync(importRate.PolId, "pol", cancellationToken);
        var poe = ResolveAsync(importRate.PoeId, "poe", cancellationToken);
        var pod = ResolveAsync(importRate.PodId, "pod", cancellationToken);
        var carrier = ResolveAsync(importRate.CarrierId, "carriers", cancellationToken);
        var agent = ResolveAsync(importRate.AgentId, "agents", cancellationToken);
        var container = ResolveAsync(
            importRate.ContainerTypeId,
            "container-types",
            cancellationToken
        );
        var currency = ResolveAsync(
            importRate.CurrencyId,
            "currencies",
            cancellationToken
        );

        await Task.WhenAll(profile, pol, poe, pod, carrier, agent, container, currency);

        if (
            profile.Result is null
            || pol.Result is null
            || poe.Result is null
            || pod.Result is null
            || carrier.Result is null
            || agent.Result is null
            || container.Result is null
            || currency.Result is null
        )
        {
            return false;
        }

        importRate.CorrectCatalogReferences(
            profile.Result,
            pol.Result,
            poe.Result,
            pod.Result,
            carrier.Result,
            agent.Result,
            container.Result,
            currency.Result,
            updatedBy
        );
        return true;
    }

    private async Task<CatalogSnapshot?> ResolveAsync(
        Guid id,
        string expectedGroup,
        CancellationToken cancellationToken
    )
    {
        var item = await configCatalog.GetActiveByIdAsync(id, cancellationToken);
        return
            item is not null
            && item.CatalogGroupSlug.Equals(
                expectedGroup,
                StringComparison.OrdinalIgnoreCase
            )
            ? CatalogSnapshot.Create(item.Id, item.Name, item.Code, item.Slug)
            : null;
    }
}

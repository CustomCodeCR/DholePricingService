using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.UpdateRate;

public sealed class UpdateRateCommandHandler(
    IRateHeaderRepository rateHeaders,
    IRateFixedCostSynchronizer fixedCostSynchronizer,
    IRateExtraDetailResolver extraDetailResolver,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateRateCommand, Result>
{
    public async Task<Result> HandleAsync(
        UpdateRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var rate = await rateHeaders.GetByIdWithDetailsAsync(command.Id, cancellationToken);

        if (rate is null || rate.IsDeleted)
        {
            return Result.Failure(PricingErrors.RateHeaderNotFound);
        }

        var existingDetails = rate.RateDetails.ToDictionary(x => x.Id);

        var extraDetails = command.ExtraDetails ?? Array.Empty<UpsertRateExtraDetailCommandItem>();

        var removedIds = (command.RemovedExtraDetailIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToHashSet();

        var updatedIds = extraDetails.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();

        if (updatedIds.Distinct().Count() != updatedIds.Length)
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }

        if (updatedIds.Any(removedIds.Contains))
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }

        foreach (var requestedDetail in extraDetails.Where(x => x.Id.HasValue))
        {
            var id = requestedDetail.Id!.Value;

            if (!existingDetails.TryGetValue(id, out var detail))
            {
                return Result.Failure(PricingErrors.RateCostDetailNotFound);
            }

            if (IsAutomaticFixed(detail) && detail.CostId != requestedDetail.CostId)
            {
                return Result.Failure(PricingErrors.RateCostDetailFixedLocked);
            }
        }

        foreach (var id in removedIds)
        {
            if (!existingDetails.TryGetValue(id, out var detail))
            {
                return Result.Failure(PricingErrors.RateCostDetailNotFound);
            }

            if (IsAutomaticFixed(detail))
            {
                return Result.Failure(PricingErrors.RateCostDetailFixedLocked);
            }
        }

        var resolvedDetails = new List<ResolvedRateExtraDetail>();

        foreach (var detail in extraDetails)
        {
            var resolution = await extraDetailResolver.ResolveAsync(
                new RateExtraDetailInput(
                    detail.Id,
                    detail.CostId,
                    detail.Name,
                    detail.CostDetailType,
                    detail.CostType,
                    detail.CurrencyId,
                    detail.CurrencyName,
                    detail.CurrencyCode,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Notes
                ),
                cancellationToken
            );

            if (!resolution.IsSuccess)
            {
                return Result.Failure(resolution.Error!);
            }

            resolvedDetails.Add(resolution.Detail!);
        }

        var selectorsChanged =
            rate.AgentId != command.AgentId
            || rate.CarrierId != command.CarrierId
            || rate.PolId != command.PolId
            || rate.PoeId != command.PoeId
            || rate.PodId != command.PodId;

        var headerBefore = PricingAuditSnapshots.From(rate);

        var detailBefore = updatedIds
            .Concat(removedIds)
            .Distinct()
            .ToDictionary(id => id, id => PricingAuditSnapshots.From(existingDetails[id]));

        var addedDetails = new List<RateDetail>();
        var modifiedDetails = new List<RateDetail>();

        try
        {
            rate.Update(
                command.AgentId,
                command.AgentName,
                command.AgentCode,
                command.CarrierId,
                command.CarrierName,
                command.CarrierCode,
                command.PolId,
                command.PolName,
                command.PolCode,
                command.PoeId,
                command.PoeName,
                command.PoeCode,
                command.PodId,
                command.PodName,
                command.PodCode,
                command.ContainerTypeId,
                command.ContainerTypeName,
                command.ContainerTypeCode,
                command.CurrencyId,
                command.CurrencyName,
                command.CurrencyCode,
                command.FreeDays,
                command.ValidFrom,
                command.ValidTo,
                command.UpdatedBy
            );

            if (selectorsChanged)
            {
                await fixedCostSynchronizer.SynchronizeAsync(
                    rate,
                    command.UpdatedBy,
                    cancellationToken
                );
            }

            foreach (var id in removedIds)
            {
                rate.RemoveRateDetail(id, command.UpdatedBy);
            }

            foreach (var detail in resolvedDetails)
            {
                if (detail.Id.HasValue)
                {
                    rate.UpdateRateDetail(
                        detail.Id.Value,
                        detail.CostId,
                        detail.Name,
                        detail.CostDetailType,
                        detail.CostType,
                        detail.CurrencyId,
                        detail.CurrencyName,
                        detail.CurrencyCode,
                        detail.CostAmount,
                        detail.SaleAmount,
                        detail.Notes,
                        command.UpdatedBy
                    );

                    modifiedDetails.Add(rate.RateDetails.First(x => x.Id == detail.Id.Value));
                }
                else
                {
                    var added = rate.AddRateDetail(
                        rate.Id,
                        detail.CostId,
                        detail.Name,
                        detail.CostDetailType,
                        detail.CostType,
                        detail.CurrencyId,
                        detail.CurrencyName,
                        detail.CurrencyCode,
                        detail.CostAmount,
                        detail.SaleAmount,
                        detail.Notes,
                        command.UpdatedBy
                    );

                    addedDetails.Add(added);
                }
            }

            rate.SetAmounts(command.UpdatedBy);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.RateHeaderUpdated,
                Action: PricingAuditActions.Updated,
                EntityType: PricingAuditEntityTypes.RateHeader,
                EntityId: rate.Id,
                ActorUserId: command.UpdatedBy,
                Before: headerBefore,
                After: PricingAuditSnapshots.From(rate),
                Payload: new
                {
                    SelectorsChanged = selectorsChanged,
                    AddedDetailIds = addedDetails.Select(x => x.Id).ToArray(),
                    UpdatedDetailIds = modifiedDetails.Select(x => x.Id).ToArray(),
                    RemovedDetailIds = removedIds.ToArray(),
                    rate.TotalCostAmount,
                    rate.TotalSaleAmount,
                    rate.TotalUtilityAmount,
                    rate.MarginPercentage,
                    rate.RequiredApproval,
                    Status = rate.Status.ToString(),
                }
            ),
            cancellationToken
        );

        foreach (var detail in addedDetails)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateDetailAdded,
                    Action: PricingAuditActions.Added,
                    EntityType: PricingAuditEntityTypes.RateDetail,
                    EntityId: detail.Id,
                    ActorUserId: command.UpdatedBy,
                    After: PricingAuditSnapshots.From(detail),
                    Payload: new
                    {
                        RateHeaderId = rate.Id,
                        RateDetailId = detail.Id,
                        detail.CostId,
                        CostType = detail.CostType.ToString(),
                        CostDetailType = detail.CostDetailType.ToString(),
                    }
                ),
                cancellationToken
            );
        }

        foreach (var detail in modifiedDetails)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateDetailUpdated,
                    Action: PricingAuditActions.Updated,
                    EntityType: PricingAuditEntityTypes.RateDetail,
                    EntityId: detail.Id,
                    ActorUserId: command.UpdatedBy,
                    Before: detailBefore[detail.Id],
                    After: PricingAuditSnapshots.From(detail),
                    Payload: new
                    {
                        RateHeaderId = rate.Id,
                        RateDetailId = detail.Id,
                        detail.CostId,
                        detail.CostAmount,
                        detail.SaleAmount,
                        detail.UtilityAmount,
                    }
                ),
                cancellationToken
            );
        }

        foreach (var removedId in removedIds)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateDetailRemoved,
                    Action: PricingAuditActions.Removed,
                    EntityType: PricingAuditEntityTypes.RateDetail,
                    EntityId: removedId,
                    ActorUserId: command.UpdatedBy,
                    Before: detailBefore[removedId],
                    Payload: new { RateHeaderId = rate.Id, RateDetailId = removedId }
                ),
                cancellationToken
            );
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);

        return Result.Success();
    }

    private static bool IsAutomaticFixed(RateDetail detail)
    {
        return detail.CostId.HasValue && detail.CostType == CostType.Fixed;
    }
}

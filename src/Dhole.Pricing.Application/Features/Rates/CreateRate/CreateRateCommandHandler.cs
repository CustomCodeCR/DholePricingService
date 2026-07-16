using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.CreateRate;

public sealed class CreateRateCommandHandler(
    IRateHeaderRepository rateHeaders,
    IImportFclRateRepository importedRates,
    IRateCodeGenerator rateCodeGenerator,
    IRateFixedCostSynchronizer fixedCostSynchronizer,
    IRateExtraDetailResolver extraDetailResolver,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IImportRateCacheService importCache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreateRateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var resolvedDetails = new List<ResolvedRateExtraDetail>();

        foreach (var detail in command.Details)
        {
            var resolution = await extraDetailResolver.ResolveAsync(
                new RateExtraDetailInput(
                    Id: null,
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
                return Result.Failure<Guid>(resolution.Error!);
            }

            resolvedDetails.Add(resolution.Detail!);
        }

        ImportFclRates? importedRate = null;
        var automaticallyApprovedImport = false;

        if (command.SourceImportFclRateId.HasValue)
        {
            var sourceId = command.SourceImportFclRateId.Value;

            importedRate = await importedRates.GetByIdAsync(sourceId, cancellationToken);

            if (importedRate is null || importedRate.IsDeleted)
            {
                return Result.Failure<Guid>(PricingErrors.ImportFclRateNotFound);
            }

            if (importedRate.Status == ImportStatus.Pending)
            {
                if (!command.CanApproveImportedRate)
                {
                    return Result.Failure<Guid>(PricingErrors.ImportFclRateInvalidStatus);
                }

                importedRate.Approve(command.CreatedBy);
                automaticallyApprovedImport = true;
            }

            if (importedRate.Status != ImportStatus.Approved)
            {
                return Result.Failure<Guid>(PricingErrors.ImportFclRateInvalidStatus);
            }
        }

        var rateConsecutive = await rateCodeGenerator.GetNextAsync(cancellationToken);

        RateHeader rate;

        try
        {
            rate = importedRate is null
                ? CreateManualRate(command, rateConsecutive)
                : CreateFromImportedRate(command, importedRate.Id, rateConsecutive);

            if (importedRate is not null)
            {
                AddImportedFreight(rate, importedRate, command, command.CreatedBy);
            }

            foreach (var detail in resolvedDetails)
            {
                rate.AddRateDetail(
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
                    quantity: 1,
                    command.CreatedBy
                );
            }

            await fixedCostSynchronizer.SynchronizeAsync(
                rate,
                command.CreatedBy,
                cancellationToken
            );

            rate.SetAmounts(command.CreatedBy);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
        }

        await rateHeaders.AddAsync(rate, cancellationToken);

        if (importedRate is not null)
        {
            importedRate.CreatedAsRate(rate.Id, command.CreatedBy);
        }

        if (importedRate is not null && automaticallyApprovedImport)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.ImportFclRateApproved,
                    Action: PricingAuditActions.Approved,
                    EntityType: PricingAuditEntityTypes.ImportFclRate,
                    EntityId: importedRate.Id,
                    ActorUserId: command.CreatedBy,
                    After: PricingAuditSnapshots.From(importedRate),
                    Payload: new { importedRate.Id, AutomaticallyApproved = true }
                ),
                cancellationToken
            );
        }

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.RateHeaderCreated,
                Action: PricingAuditActions.Created,
                EntityType: PricingAuditEntityTypes.RateHeader,
                EntityId: rate.Id,
                ActorUserId: command.CreatedBy,
                After: PricingAuditSnapshots.From(rate),
                Payload: new
                {
                    rate.Id,
                    rate.SourceImportFclRateId,
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

        foreach (var detail in rate.RateDetails)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateDetailAdded,
                    Action: PricingAuditActions.Added,
                    EntityType: PricingAuditEntityTypes.RateDetail,
                    EntityId: detail.Id,
                    ActorUserId: command.CreatedBy,
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

        if (importedRate is not null)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.ImportFclRateCreatedAsRate,
                    Action: PricingAuditActions.CreatedAsRate,
                    EntityType: PricingAuditEntityTypes.ImportFclRate,
                    EntityId: importedRate.Id,
                    ActorUserId: command.CreatedBy,
                    After: PricingAuditSnapshots.From(importedRate),
                    Payload: new { ImportedRateId = importedRate.Id, RateHeaderId = rate.Id }
                ),
                cancellationToken
            );
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);

        if (importedRate is not null)
        {
            await importCache.RemoveImportRateCacheAsync(
                importedRate.Id,
                importedRate.ImportBatchId,
                cancellationToken
            );
        }

        return Result.Success(rate.Id);
    }

    private static RateHeader CreateManualRate(CreateRateCommand command, long rateConsecutive)
    {
        return RateHeader.Create(
            rateConsecutive,
            sourceImportFclRateId: null,
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
            containerQuantity: 1,
            command.CreatedBy
        );
    }

    private static RateHeader CreateFromImportedRate(
        CreateRateCommand command,
        Guid importedRateId,
        long rateConsecutive
    )
    {
        return RateHeader.Create(
            rateConsecutive,
            importedRateId,
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
            containerQuantity: 1,
            command.CreatedBy
        );
    }

    private static void AddImportedFreight(
        RateHeader rate,
        ImportFclRates importedRate,
        CreateRateCommand command,
        Guid? createdBy
    )
    {
        var costAmount = importedRate.OceanFreight ?? importedRate.Freight;

        var saleAmount =
            importedRate.TotalSale ?? importedRate.OceanFreight ?? importedRate.Freight;

        rate.AddRateDetail(
            rate.Id,
            costId: null,
            name: "Flete internacional",
            CostDetailType.Freight,
            CostType.Variable,
            command.CurrencyId,
            command.CurrencyName,
            command.CurrencyCode,
            costAmount,
            saleAmount,
            importedRate.RawDataJson,
            quantity: 1,
            createdBy
        );
    }
}

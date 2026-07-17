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

namespace Dhole.Pricing.Application.Features.Rates.DuplicateRate;

public sealed class DuplicateRateCommandHandler(
    IRateHeaderRepository rateHeaders,
    IRateCodeGenerator rateCodeGenerator,
    IRateFixedCostSynchronizer fixedCostSynchronizer,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<DuplicateRateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        DuplicateRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var source = await rateHeaders.GetByIdWithDetailsAsync(command.Id, cancellationToken);

        if (source is null || source.IsDeleted)
        {
            return Result.Failure<Guid>(PricingErrors.RateHeaderNotFound);
        }

        var rateConsecutive = await rateCodeGenerator.GetNextAsync(cancellationToken);

        RateHeader duplicate;

        try
        {
            duplicate = RateHeader.Create(
                rateConsecutive,
                sourceImportFclRateId: null,
                source.AgentId,
                source.AgentName,
                source.AgentCode,
                source.CarrierId,
                source.CarrierName,
                source.CarrierCode,
                source.PolId,
                source.PolName,
                source.PolCode,
                source.PoeId,
                source.PoeName,
                source.PoeCode,
                source.PodId,
                source.PodName,
                source.PodCode,
                source.ContainerTypeId,
                source.ContainerTypeName,
                source.ContainerTypeCode,
                source.CurrencyId,
                source.CurrencyName,
                source.CurrencyCode,
                source.FreeDays,
                command.ValidFrom ?? source.ValidFrom,
                command.ValidTo ?? source.ValidTo,
                source.ContainerQuantity > 0 ? source.ContainerQuantity : 1,
                source.ClientName,
                null,
                null,
                source.Includes,
                source.SubjectTo,
                source.Excludes,
                source.TransitDays,
                command.CreatedBy
            );

            var copiedDetails = source.RateDetails.Where(x =>
                !x.CostId.HasValue || x.CostType != CostType.Fixed
            );

            foreach (var detail in copiedDetails)
            {
                duplicate.AddRateDetail(
                    duplicate.Id,
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
                    detail.Quantity > 0 ? detail.Quantity : 1,
                    command.CreatedBy
                );
            }

            await fixedCostSynchronizer.SynchronizeAsync(
                duplicate,
                command.CreatedBy,
                cancellationToken
            );

            duplicate.SetAmounts(command.CreatedBy);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
        }

        await rateHeaders.AddAsync(duplicate, cancellationToken);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.RateHeaderCreated,
                Action: PricingAuditActions.Created,
                EntityType: PricingAuditEntityTypes.RateHeader,
                EntityId: duplicate.Id,
                ActorUserId: command.CreatedBy,
                After: PricingAuditSnapshots.From(duplicate),
                Payload: new
                {
                    SourceRateHeaderId = source.Id,
                    NewRateHeaderId = duplicate.Id,
                    duplicate.TotalCostAmount,
                    duplicate.TotalSaleAmount,
                    duplicate.TotalUtilityAmount,
                    duplicate.MarginPercentage,
                    duplicate.RequiredApproval,
                    Status = duplicate.Status.ToString(),
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveRateHeaderCacheAsync(duplicate.Id, cancellationToken);

        return Result.Success(duplicate.Id);
    }
}

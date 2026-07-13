using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Imports.CreateImportRate;

public sealed class CreateImportRateCommandHandler(
    IImportFclRateRepository importRates,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreateImportRateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateImportRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        ImportFclRates importRate;

        try
        {
            importRate = ImportFclRates.Create(
                command.ImportBatchId,
                command.ExtractionRecordId,
                command.SourceType,
                command.Profile,
                command.Pol,
                command.Poe,
                command.Pod,
                command.Carrier,
                command.Agent,
                command.ContainerType,
                command.Currency,
                command.Commodity,
                command.OceanFreight,
                command.OriginCharges,
                command.DestinationCharges,
                command.Surcharges,
                command.TotalCost,
                command.TotalSale,
                command.Profit,
                command.Margin,
                command.FreeDays,
                command.TransitDays,
                command.ValidFrom,
                command.ValidTo,
                command.RawDataJson,
                command.CreatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.InvalidImportFclRate);
        }

        await importRates.AddAsync(importRate, cancellationToken);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.ImportFclRateCreated,
                Action: PricingAuditActions.Created,
                EntityType: PricingAuditEntityTypes.ImportFclRate,
                EntityId: importRate.Id,
                ActorUserId: command.CreatedBy,
                After: PricingAuditSnapshots.From(importRate),
                Payload: PricingAuditSnapshots.From(importRate)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveImportRateCacheAsync(
            importRate.Id,
            importRate.ImportBatchId,
            cancellationToken
        );

        return Result.Success(importRate.Id);
    }
}

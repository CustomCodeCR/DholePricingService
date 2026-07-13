using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.DeleteRate;

public sealed class DeleteRateCommandHandler(
    IRateHeaderRepository rateHeaders,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<DeleteRateCommand, Result>
{
    public async Task<Result> HandleAsync(
        DeleteRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var ids = command.Ids.Where(x => x != Guid.Empty).Distinct().ToArray();

        if (ids.Length == 0)
        {
            return Result.Failure(PricingErrors.RateHeaderNotFound);
        }

        var entities = new List<RateHeader>();

        foreach (var id in ids)
        {
            var rate = await rateHeaders.GetByIdWithDetailsAsync(id, cancellationToken);

            if (rate is null || rate.IsDeleted)
            {
                return Result.Failure(PricingErrors.RateHeaderNotFound);
            }

            entities.Add(rate);
        }

        foreach (var rate in entities)
        {
            var before = PricingAuditSnapshots.From(rate);

            rate.Delete(command.DeletedBy);

            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateHeaderDeleted,
                    Action: PricingAuditActions.Deleted,
                    EntityType: PricingAuditEntityTypes.RateHeader,
                    EntityId: rate.Id,
                    ActorUserId: command.DeletedBy,
                    Before: before,
                    After: PricingAuditSnapshots.From(rate),
                    Payload: new { rate.Id, rate.SourceImportFclRateId }
                ),
                cancellationToken
            );
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var rate in entities)
        {
            await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);
        }

        return Result.Success();
    }
}

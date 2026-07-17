using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.SetRateStatus;

public sealed class SetRateStatusCommandHandler(
    IRateHeaderRepository rateHeaders,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<SetRateStatusCommand, Result>
{
    public async Task<Result> HandleAsync(
        SetRateStatusCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var rate = await rateHeaders.GetByIdWithDetailsAsync(command.Id, cancellationToken);

        if (rate is null || rate.IsDeleted)
        {
            return Result.Failure(PricingErrors.RateHeaderNotFound);
        }

        var before = PricingAuditSnapshots.From(rate);

        try
        {
            rate.SetCommercialStatus(command.Status, command.UpdatedBy);
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
                Before: before,
                After: PricingAuditSnapshots.From(rate),
                Payload: new { rate.Id, Status = rate.Status.ToString() }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);

        return Result.Success();
    }
}

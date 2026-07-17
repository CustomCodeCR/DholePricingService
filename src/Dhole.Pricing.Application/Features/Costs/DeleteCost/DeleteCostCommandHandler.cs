using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Costs.Delete;

public sealed class DeleteCostCommandHandler(
    ICostRepository costs,
    IPricingAuditService audit,
    ICostCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<DeleteCostCommand, Result>
{
    public async Task<Result> HandleAsync(
        DeleteCostCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var cost = await costs.GetByIdAsync(command.Id, cancellationToken);

        if (cost is null || cost.IsDeleted)
        {
            return Result.Failure(PricingErrors.CostNotFound);
        }

        var before = PricingAuditSnapshots.From(cost);

        cost.Delete(command.DeletedBy);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.CostDeleted,
                Action: PricingAuditActions.Deleted,
                EntityType: PricingAuditEntityTypes.Cost,
                EntityId: cost.Id,
                ActorUserId: command.DeletedBy,
                Before: before,
                After: PricingAuditSnapshots.From(cost),
                Payload: PricingAuditSnapshots.From(cost)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveCostCacheAsync(cost.Id, cancellationToken);

        return Result.Success();
    }
}

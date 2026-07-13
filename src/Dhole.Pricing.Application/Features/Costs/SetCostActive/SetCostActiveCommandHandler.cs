using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Costs.SetActive;

public sealed class SetCostActiveCommandHandler(
    ICostRepository costs,
    IPricingAuditService audit,
    ICostCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<SetCostActiveCommand, Result>
{
    public async Task<Result> HandleAsync(
        SetCostActiveCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var cost = await costs.GetByIdAsync(command.Id, cancellationToken);

        if (cost is null || cost.IsDeleted)
        {
            return Result.Failure(PricingErrors.CostNotFound);
        }

        if (cost.IsActive == command.IsActive)
        {
            return Result.Success();
        }

        var before = PricingAuditSnapshots.From(cost);

        cost.SetActive(command.IsActive, command.UpdatedBy);

        var eventType = command.IsActive
            ? PricingAuditEventTypes.CostActivated
            : PricingAuditEventTypes.CostInactivated;

        var action = command.IsActive
            ? PricingAuditActions.Activated
            : PricingAuditActions.Inactivated;

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: eventType,
                Action: action,
                EntityType: PricingAuditEntityTypes.Cost,
                EntityId: cost.Id,
                ActorUserId: command.UpdatedBy,
                Before: before,
                After: PricingAuditSnapshots.From(cost),
                Payload: PricingAuditSnapshots.From(cost)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveCostsSelectAsync(cancellationToken);

        return Result.Success();
    }
}

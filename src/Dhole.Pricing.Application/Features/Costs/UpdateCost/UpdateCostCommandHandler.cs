using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Costs.Update;

public sealed class UpdateCostCommandHandler(
    ICostRepository costs,
    IPricingAuditService audit,
    ICostCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateCostCommand, Result>
{
    public async Task<Result> HandleAsync(
        UpdateCostCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var cost = await costs.GetByIdAsync(command.Id, cancellationToken);

        if (cost is null || cost.IsDeleted)
        {
            return Result.Failure(PricingErrors.CostNotFound);
        }

        var alreadyExists = await costs.ExistsByNameAsync(
            command.Name,
            command.CostType,
            command.CostDetailType,
            command.PortId,
            command.PortRole,
            command.CarrierId,
            command.AgentId,
            command.Id,
            cancellationToken
        );

        if (alreadyExists)
        {
            return Result.Failure(PricingErrors.CostAlreadyExists);
        }

        var before = PricingAuditSnapshots.From(cost);

        try
        {
            cost.Update(
                command.Name,
                command.CostType,
                command.CostDetailType,
                command.CarrierId,
                command.CarrierName,
                command.CarrierCode,
                command.AgentId,
                command.AgentName,
                command.AgentCode,
                command.PortId,
                command.PortName,
                command.PortCode,
                command.PortRole,
                command.CurrencyId,
                command.CurrencyName,
                command.CurrencyCode,
                command.CostAmount,
                command.SaleAmount,
                command.Notes,
                command.IsAccountant,
                command.UpdatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(PricingErrors.InvalidCost);
        }

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.CostUpdated,
                Action: PricingAuditActions.Updated,
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

        await cache.RemoveCostCacheAsync(cost.Id, cancellationToken);

        return Result.Success();
    }
}

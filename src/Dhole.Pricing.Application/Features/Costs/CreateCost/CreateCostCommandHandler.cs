using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Costs.Create;

public sealed class CreateCostCommandHandler(
    ICostRepository costs,
    IPricingAuditService audit,
    ICostCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreateCostCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateCostCommand command,
        CancellationToken cancellationToken = default
    )
    {
        if (
            await costs.ExistsByNameAsync(
                command.Name,
                command.CostType,
                command.CostDetailType,
                command.PortId,
                command.PortRole,
                command.CarrierId,
                command.AgentId,
                null,
                cancellationToken
            )
        )
        {
            return Result.Failure<Guid>(PricingErrors.CostAlreadyExists);
        }

        Cost cost;

        try
        {
            cost = Cost.Create(
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
                command.CreatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.InvalidCost);
        }

        await costs.AddAsync(cost, cancellationToken);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.CostCreated,
                Action: PricingAuditActions.Created,
                EntityType: PricingAuditEntityTypes.Cost,
                EntityId: cost.Id,
                ActorUserId: command.CreatedBy,
                After: PricingAuditSnapshots.From(cost),
                Payload: PricingAuditSnapshots.From(cost)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveCostCacheAsync(cost.Id, cancellationToken);

        return Result.Success(cost.Id);
    }
}

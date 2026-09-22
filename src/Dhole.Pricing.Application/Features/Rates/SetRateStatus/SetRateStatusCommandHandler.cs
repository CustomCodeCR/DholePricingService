using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Rates.Enums;
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

        if (
            rate.RateType == RateType.Tariff
            && rate.ClientName?.Contains("TARIFARIO", StringComparison.OrdinalIgnoreCase) == true
            && command.Status is RateStatus.AcceptedByClient or RateStatus.RejectedByClient
        )
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }

        var advancesQuotation = command.Status is
            RateStatus.Open or
            RateStatus.Sent or
            RateStatus.RequestedByClient or
            RateStatus.AcceptedByClient;

        // Una tarifa comercial con margen inferior al 12% debe permanecer pendiente
        // hasta que un usuario con pricing.rate.approve-low-margin la apruebe.
        // Las solicitudes abiertas sin venta todavía pueden pasar a Open para que Pricing
        // complete proveedor/costos sin convertirlas en una cotización utilizable.
        if (rate.RequiredApproval && rate.TotalSaleAmount > 0m && advancesQuotation)
        {
            return Result.Failure(PricingErrors.RateLowMarginRequiresApproval);
        }

        if (command.Status == RateStatus.Closed
            && string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure(PricingErrors.RateClosureReasonIsRequired);
        }

        if (command.Status == RateStatus.AcceptedByClient
            && rate.Status != RateStatus.AcceptedByClient)
        {
            var capacity = await rateHeaders.GetOwnLclCapacityForRateAsync(
                rate.Id,
                cancellationToken
            );

            if (capacity is not null
                && capacity.RequestedCbm > capacity.RemainingCbm + 0.000001m)
            {
                return Result.Failure(
                    PricingErrors.OwnLclCapacityExceeded(
                        capacity.ConsolidationNumber,
                        capacity.RequestedCbm,
                        capacity.RemainingCbm
                    )
                );
            }
        }

        var before = PricingAuditSnapshots.From(rate);

        try
        {
            if (command.Status == RateStatus.AcceptedByClient
                && !string.IsNullOrWhiteSpace(command.IdtraNumber))
            {
                rate.SetIdtraNumber(command.IdtraNumber, command.UpdatedBy);
            }

            rate.SetCommercialStatus(
                command.Status,
                command.Reason,
                command.UpdatedBy,
                command.AllowDirectClientDecision
            );
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
                Payload: new
                {
                    rate.Id,
                    Status = rate.Status.ToString(),
                    rate.IdtraNumber,
                    rate.ClosedReason,
                    rate.ClosedAtUtc,
                    rate.ClosedBy,
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);

        return Result.Success();
    }
}

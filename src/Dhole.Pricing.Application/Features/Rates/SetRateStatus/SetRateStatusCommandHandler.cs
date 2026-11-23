using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Dhole.Pricing.Application.Features.Rates.SetRateStatus;

public sealed class SetRateStatusCommandHandler(
    IRateHeaderRepository rateHeaders,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutboxWriter outbox,
    ILogger<SetRateStatusCommandHandler> logger
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

        // El vínculo se valida en el servidor, nunca se confía en un texto/ID enviado por la UI.
        if (command.Status == RateStatus.QUOVinculada)
        {
            if (command.LinkedAcceptedRateId is not { } acceptedId || acceptedId == Guid.Empty || acceptedId == rate.Id)
                return Result.Failure(PricingErrors.RateInvalidStatus);

            var accepted = await rateHeaders.GetByIdWithDetailsAsync(acceptedId, cancellationToken);
            if (accepted is null || !RateAcceptedQuotationLinkPolicy.CanLink(rate, accepted))
                return Result.Failure(PricingErrors.RateInvalidStatus);
        }
        else if (command.LinkedAcceptedRateId.HasValue)
        {
            // Una relación nunca puede escribirse accidentalmente para otro estado.
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

        if ((command.Status is RateStatus.Closed or RateStatus.Cancelled)
            && string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure(PricingErrors.RateClosureReasonIsRequired);
        }

        var previouslyAccepted = rate.Status == RateStatus.AcceptedByClient;
        var before = PricingAuditSnapshots.From(rate);

        try
        {
            if (command.Status == RateStatus.AcceptedByClient
                && !string.IsNullOrWhiteSpace(command.IdtraNumber))
            {
                rate.SetIdtraNumber(command.IdtraNumber, command.UpdatedBy);
            }

            if (command.Status == RateStatus.AcceptedByClient && command.OtherReferences is not null)
                rate.SetOtherReferences(command.OtherReferences, command.UpdatedBy);

            rate.SetCommercialStatus(
                command.Status,
                command.Reason,
                command.UpdatedBy,
                command.AllowDirectClientDecision,
                command.LinkedAcceptedRateId
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
                    rate.LinkedAcceptedRateId,
                    rate.IdtraNumber,
                    rate.ClosedReason,
                    rate.ClosedAtUtc,
                    rate.ClosedBy,
                    AcceptanceObservations = command.Status == RateStatus.AcceptedByClient
                        ? command.AcceptanceObservations : null,
                }
            ),
            cancellationToken
        );

        // Persist the definitive accepted commercial snapshot in the SAME database
        // transaction as the status change. Redis publication is asynchronous.
        if (command.Status == RateStatus.AcceptedByClient && !previouslyAccepted
            && rate.RateType != RateType.Tariff)
        {
            await outbox.WriteAsync(
                "pricing.quote.accepted.v1",
                "pricing.quote.accepted.v1",
                new
                {
                    schemaVersion = 1,
                    quoteId = rate.Id,
                    selectedQuoteOptionId = rate.Id,
                    quoteNumber = rate.QuoNumber ?? rate.RateCode,
                    revisionNumber = rate.RevisionNumber,
                    acceptedAtUtc = rate.AcceptedAtUtc,
                    acceptedBy = command.UpdatedBy,
                    accountId = rate.ClientAccountId,
                    clientLeadId = rate.ClientLeadId,
                    clientName = rate.ClientName,
                    executiveName = rate.ExecutiveName,
                    executiveUserId = command.ExecutiveUserId,
                    modalityCode = rate.ShipmentMode.ToString().ToUpperInvariant(),
                    operationType = rate.OperationType.ToString(),
                    incoterm = rate.IncotermCode,
                    pol = new { id = rate.PolId, name = rate.PolName, code = rate.PolCode },
                    poe = new { id = rate.PoeId, name = rate.PoeName, code = rate.PoeCode },
                    pod = new { id = rate.PodId, name = rate.PodName, code = rate.PodCode },
                    carrier = new { id = rate.CarrierId, name = rate.CarrierName, code = rate.CarrierCode },
                    agent = new { id = rate.AgentId, name = rate.AgentName, code = rate.AgentCode },
                    equipment = rate.RateContainers.Select(x => new
                    {
                        equipmentType = x.ContainerTypeCode,
                        equipmentName = x.ContainerTypeName,
                        quantity = x.Quantity
                    }).ToArray(),
                    quotedWeightKg = rate.TotalWeightKg,
                    quotedVolumeCbm = rate.TotalVolumeCbm,
                    packages = rate.TotalPackages,
                    cargoLinesJson = rate.CargoLinesJson,
                    currency = rate.CurrencyCode,
                    includes = rate.Includes,
                    excludes = rate.Excludes,
                    subjectTo = rate.SubjectTo,
                    pickupAddress = rate.PickupAddress,
                    pickupLocationsJson = rate.PickupLocationsJson,
                    inlandServiceLocationsJson = rate.InlandServiceLocationsJson,
                    transitTime = rate.TransitTime,
                    totalCost = rate.TotalCostAmount,
                    totalSale = rate.TotalSaleAmount,
                    services = rate.RateServices.Select(x => new
                    {
                        id = x.ServiceId, code = x.ServiceCode, name = x.ServiceName
                    }).ToArray(),
                    costs = rate.RateDetails.Select(x => new
                    {
                        sourceCostId = x.Id,
                        catalogCostId = x.CostId,
                        code = x.CostDetailType.ToString(),
                        description = x.Name,
                        currency = x.CurrencyCode,
                        unitCost = x.CostAmount,
                        unitSale = x.SaleAmount,
                        quantity = x.Quantity,
                        cost = x.CostAmount * x.Quantity,
                        sale = x.SaleAmount * x.Quantity,
                        notes = x.Notes,
                        billToClient = x.BillToClient,
                        chargeBasis = x.ChargeBasis.ToString()
                    }).ToArray()
                },
                $"operations-opening:{rate.Id:N}",
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // La transición ya está persistida: una caída temporal de Redis no debe
        // convertir una aceptación exitosa en HTTP 500 ni provocar reintentos.
        try
        {
            await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Estado comercial guardado para tarifa {RateId}, pero falló la invalidación de caché.",
                rate.Id
            );
        }

        return Result.Success();
    }
}

using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

// A seller's work item is completed when Pricing finishes the quote.
// Delivery may have to wait for a margin approval or a transient PDF/outbox issue.
// Reconciliation is server-side so the seller need not keep a browser tab open.
public sealed class SellerRateDeliveryRetryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SellerRateDeliveryRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "No se pudieron reconciliar los envíos pendientes de solicitudes de vendedores.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task DeliverPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ServiceDbContext>();

        // Filter approvals in SQL before Take: a long queue of pending-margin
        // requests must not starve quotes that are ready to be delivered.
        var candidates = await (
            from request in db.RateRequests.AsNoTracking()
            join rate in db.RateHeaders.AsNoTracking() on request.RateId equals (Guid?)rate.Id
            where ((request.Status == RateRequestStatus.Completed
                        && request.DeliveryConfirmedAtUtc == null)
                   // Recover older completed quotations that stayed Open because
                   // the user was previously required to mark them manually.
                   || (request.Status == RateRequestStatus.Open && rate.TotalSaleAmount > 0m))
                  && !rate.IsDeleted
                  && (rate.Status == RateStatus.Open
                      || rate.Status == RateStatus.ApprovedByManagement
                      || rate.Status == RateStatus.Sent)
                  && (!rate.RequiredApproval || rate.TotalSaleAmount <= 0m)
            orderby request.RequestedAtUtc
            select new { rate.Id, request.Status, request.PayloadJson })
            .Take(500)
            .ToListAsync(cancellationToken);

        var rateIds = candidates
            .Where(x => x.Status == RateRequestStatus.Completed
                || !RateModificationRequestPayload.IsModification(x.PayloadJson))
            .Select(x => x.Id)
            .Distinct()
            .Take(20)
            .ToArray();

        if (rateIds.Count == 0) return;

        var dispatcher = services.GetRequiredService<ICommandDispatcher>();
        var outbox = services.GetRequiredService<IIntegrationEventOutboxWriter>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        foreach (var rateId in rateIds)
        {
            try
            {
                await RateRequestCompletionEndpoints.TryCompleteApprovedRateAsync(
                    rateId, db, dispatcher, outbox, new DefaultHttpContext(),
                    loggerFactory, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "La tarifa {RateId} seguirá en reintentos de entrega automática.", rateId);
            }
        }
    }
}

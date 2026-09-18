using System.Net;
using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class SellerRateStatusEndpoints
{
    private const string OpeningsEmail = "aperturas@grupocastrofallas.com";
    private const string NotificationEventName = "notifications.notification.requested";

    public static IEndpointRouteBuilder MapSellerRateStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/pricing/seller-rates/{rateId:guid}/status", SetStatusAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        return app;
    }

    private static async Task<IResult> SetStatusAsync(
        Guid rateId,
        SellerRateStatusRequest request,
        ServiceDbContext db,
        ICommandDispatcher dispatcher,
        IPricingAuditService audit,
        IRateHeaderCacheService cache,
        IIntegrationEventOutboxWriter outbox,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var currentUserId = httpContext.GetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
            return Results.Unauthorized();

        if (!Enum.TryParse<RateStatus>(request.Status, true, out var status)
            || status is not (RateStatus.AcceptedByClient or RateStatus.RejectedByClient))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.SellerInvalidRateStatus",
                message = "El vendedor únicamente puede marcar su tarifa como aceptada o rechazada.",
            });
        }

        var rateRequest = await db.RateRequests
            .AsNoTracking()
            .Where(x => x.SellerUserId == currentUserId.Value && x.RateId == rateId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (rateRequest is null)
            return Results.Forbid();

        var currentRate = await db.RateHeaders
            .Include(x => x.RateContainers)
            .Include(x => x.RateDetails)
            .Include(x => x.RateServices)
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);

        if (currentRate is null)
            return Results.NotFound();

        if (currentRate.Status is not (RateStatus.Sent or RateStatus.RequestedByClient))
        {
            return Results.Conflict(new
            {
                code = "Pricing.SellerRateNotAwaitingClient",
                message = "La tarifa solo puede aceptarse o rechazarse cuando está enviada al cliente.",
            });
        }

        if (status == RateStatus.RejectedByClient && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.SellerRejectionReasonRequired",
                message = "Indique el motivo por el que el cliente rechazó la tarifa.",
            });
        }

        if (status == RateStatus.AcceptedByClient && string.IsNullOrWhiteSpace(currentRate.IdtraNumber))
        {
            var before = PricingAuditSnapshots.From(currentRate);

            db.Entry(currentRate).Property(x => x.Status).CurrentValue = RateStatus.AcceptedByClient;
            db.Entry(currentRate).Property(x => x.RequiredApproval).CurrentValue = false;

            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateHeaderUpdated,
                    Action: PricingAuditActions.Updated,
                    EntityType: PricingAuditEntityTypes.RateHeader,
                    EntityId: currentRate.Id,
                    ActorUserId: currentUserId,
                    Before: before,
                    After: PricingAuditSnapshots.From(currentRate),
                    Payload: new
                    {
                        currentRate.Id,
                        Status = currentRate.Status.ToString(),
                        currentRate.IdtraNumber,
                        Source = "SellerClientDecision",
                        IdtraPending = true,
                    }
                ),
                cancellationToken
            );

            await db.SaveChangesAsync(cancellationToken);
            await cache.RemoveRateHeaderCacheAsync(currentRate.Id, cancellationToken);
            await NotifyOpeningsAsync(dispatcher, outbox, db, loggerFactory, rateRequest, currentRate, cancellationToken);
            return Results.NoContent();
        }

        var result = await dispatcher.DispatchAsync(
            new SetRateStatusCommand(
                rateId,
                status,
                request.Reason,
                null,
                currentUserId
            ),
            cancellationToken
        );

        if (result.IsSuccess && status == RateStatus.AcceptedByClient)
        {
            await NotifyOpeningsAsync(dispatcher, outbox, db, loggerFactory, rateRequest, currentRate, cancellationToken);
        }

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task NotifyOpeningsAsync(
        ICommandDispatcher dispatcher,
        IIntegrationEventOutboxWriter outbox,
        ServiceDbContext db,
        ILoggerFactory loggerFactory,
        RateRequest request,
        RateHeader rate,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SellerRateOpeningsNotification");
        try
        {
            var documentResult = await dispatcher.DispatchAsync(
                new GenerateRateDocumentCommand(rate.Id, null, "pdf"),
                cancellationToken
            );
            if (!documentResult.IsSuccess)
            {
                logger.LogError(
                    "No se generó el PDF de la tarifa {RateId}; no se encolará a Aperturas un correo incompleto.",
                    rate.Id
                );
                return;
            }

            var payload = ReadRequestPayload(request.PayloadJson);
            var sellerText = request.SellerName ?? request.ExecutiveName ?? rate.ExecutiveName ?? "Ventas";
            var clientText = rate.ClientName ?? request.ClientName ?? "Cliente";
            var quoteNumber = rate.QuoNumber ?? rate.RateCode;
            var polText = string.IsNullOrWhiteSpace(rate.PolName) ? "—" : rate.PolName;
            var poeText = string.IsNullOrWhiteSpace(rate.PoeName) ? "—" : rate.PoeName;
            var podText = rate.PodName ?? request.PodName ?? "—";
            var routeText = $"{polText} → {poeText} → {podText}";
            var incotermText = rate.IncotermName ?? rate.IncotermCode ?? "—";
            var operationTypeText = OperationTypeLabel(rate.OperationType);
            var cargoTypeText = ShipmentLabel(request.ShipmentMode ?? payload.ShipmentMode);
            var transportTypeText = ResolveTransportType(payload.Modality, cargoTypeText);

            var equipmentLines = rate.RateContainers.Count == 0
                ? new[]
                {
                    new
                    {
                        quantity = payload.EquipmentQuantity == "—"
                            ? rate.ContainerQuantity.ToString()
                            : payload.EquipmentQuantity,
                        size = payload.EquipmentSize,
                        type = payload.EquipmentType == "—"
                            ? (rate.ContainerTypeName ?? rate.ContainerTypeCode ?? "—")
                            : payload.EquipmentType,
                    },
                }
                : rate.RateContainers
                    .OrderBy(x => x.ContainerTypeName)
                    .Select(x => new
                    {
                        quantity = x.Quantity.ToString(),
                        size = EquipmentSize(x.ContainerTypeName, x.ContainerTypeCode),
                        type = EquipmentType(x.ContainerTypeName, x.ContainerTypeCode),
                    })
                    .ToArray();

            var equipmentHtml = string.Join(
                "<br />",
                equipmentLines.Select(x =>
                    $"Cantidad: {Html(x.quantity)} · Tamaño: {Html(x.size)} · Tipo: {Html(x.type)}")
            );

            var serviceValues = rate.RateServices.Count == 0
                ? new[] { payload.Services }
                : rate.RateServices
                    .OrderBy(x => x.ServiceName)
                    .Select(x => string.IsNullOrWhiteSpace(x.ServiceCode)
                        ? x.ServiceName
                        : $"{x.ServiceName} ({x.ServiceCode})")
                    .ToArray();
            var servicesText = string.Join(", ", serviceValues.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (string.IsNullOrWhiteSpace(servicesText))
                servicesText = "—";

            var emailBody = $"""
                <p>Se confirmó una tarifa aprobada por el cliente. Se adjunta el PDF de la QUO aceptada para solicitar la apertura.</p>
                <table style="border-collapse:collapse; width:100%; max-width:820px">
                  <tr><td style="padding:5px 10px"><strong>Vendedor</strong></td><td style="padding:5px 10px">{Html(sellerText)}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Cliente</strong></td><td style="padding:5px 10px">{Html(clientText)}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Ruta (POL → POE → POD)</strong></td><td style="padding:5px 10px">{Html(routeText)}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Tipo</strong></td><td style="padding:5px 10px">{Html(transportTypeText)}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Tipo de carga</strong></td><td style="padding:5px 10px">{Html(cargoTypeText)}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Equipo</strong></td><td style="padding:5px 10px">{equipmentHtml}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Incoterm</strong></td><td style="padding:5px 10px">{Html(incotermText)}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Servicio</strong></td><td style="padding:5px 10px">{Html(servicesText)}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Tipo de operación</strong></td><td style="padding:5px 10px">{Html(operationTypeText)}</td></tr>
                </table>
                <p>Favor continuar con el proceso de apertura correspondiente.</p>
                """;

            var document = documentResult.Value;
            await outbox.WriteAsync(
                NotificationEventName,
                NotificationEventName,
                new
                {
                    notificationType = "pricing.rate.accepted.opening-request",
                    channel = "Email",
                    entityType = "RateHeader",
                    entityId = rate.Id.ToString(),
                    subject = $"Solicitud de apertura - {quoteNumber} - {clientText}",
                    body = emailBody,
                    payload = new
                    {
                        rateId = rate.Id,
                        quo = quoteNumber,
                        seller = sellerText,
                        client = clientText,
                        route = new
                        {
                            pol = polText,
                            poe = poeText,
                            pod = podText,
                        },
                        transportType = transportTypeText,
                        cargoType = cargoTypeText,
                        equipment = equipmentLines,
                        incoterm = incotermText,
                        services = serviceValues,
                        operationType = operationTypeText,
                        attachments = new[]
                        {
                            new
                            {
                                fileName = document.FileName,
                                contentType = document.ContentType,
                                contentBase64 = Convert.ToBase64String(document.Content),
                            },
                        },
                    },
                    maxAttempts = 5,
                    recipients = new[]
                    {
                        new
                        {
                            userId = (string?)null,
                            address = OpeningsEmail,
                            displayName = "Aperturas",
                        },
                    },
                },
                $"pricing-openings:{rate.Id:N}:rev:{rate.RevisionNumber}",
                cancellationToken
            );

            // Persistir el Outbox en Pricing; Notifications se encarga del SMTP, reintentos y adjunto.
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Se encoló correo de solicitud de apertura para tarifa {RateId} / {QuoteNumber}.",
                rate.Id,
                quoteNumber
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "No se pudo encolar en Notifications la aceptación de la tarifa {RateId}.",
                rate.Id
            );
        }
    }

    private static string ResolveTransportType(string? modality, string cargoType)
    {
        var explicitType = ModalityLabel(modality);
        if (!string.Equals(explicitType, "—", StringComparison.Ordinal))
            return explicitType;

        return cargoType switch
        {
            "FCL" or "LCL" => "Marítimo",
            "FTL" or "LTL" => "Terrestre",
            _ => "—",
        };
    }

    private static string EquipmentSize(string? name, string? code)
    {
        foreach (var value in new[] { code, name })
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var match = System.Text.RegularExpressions.Regex.Match(
                value,
                @"(?<!\d)(20|40|45)(?!\d)",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant
            );

            if (match.Success)
                return $"{match.Groups[1].Value}'";
        }

        return "—";
    }

    private static string EquipmentType(string? name, string? code)
    {
        var candidate = !string.IsNullOrWhiteSpace(name) ? name.Trim() : code?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return "—";

        candidate = System.Text.RegularExpressions.Regex.Replace(
            candidate,
            @"(?<!\d)(20|40|45)(?!\d)\s*['’]?",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant
        ).Trim(' ', '-', '/', '·');

        return string.IsNullOrWhiteSpace(candidate)
            ? (string.IsNullOrWhiteSpace(code) ? "—" : code.Trim())
            : candidate;
    }

    private static RequestPayload ReadRequestPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return RequestPayload.Empty;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var form = TryGet(root, "form", out var nestedForm) && nestedForm.ValueKind == JsonValueKind.Object
                ? nestedForm
                : root;
            var sellerContext = TryGet(root, "sellerContext", out var nestedContext) && nestedContext.ValueKind == JsonValueKind.Object
                ? nestedContext
                : default;

            var equipmentSize = Text(form, "equipmentSize");
            var equipmentType = Text(form, "equipmentType");
            var equipmentQuantity = Text(form, "equipmentQuantity") ?? Text(form, "containerQuantity") ?? Text(form, "quantity");
            var cargoReady = Text(sellerContext, "cargoReadyDate") ?? Text(form, "loadDate");
            var portHandling = Text(sellerContext, "portHandlingMode") ?? Text(form, "portHandlingMode");
            var services = ArrayText(form, "services") ?? ArrayText(form, "serviceIds") ?? Text(form, "services");

            return new RequestPayload(
                Text(form, "modality") ?? "—",
                Text(form, "shipmentMode") ?? "—",
                equipmentSize ?? "—",
                equipmentType ?? "—",
                equipmentQuantity ?? "1",
                cargoReady ?? "—",
                Text(form, "cargoDescription") ?? Text(form, "cargoObservations") ?? "—",
                Text(form, "cargoValue") ?? "—",
                portHandling ?? "—",
                services ?? "—"
            );
        }
        catch (JsonException)
        {
            return RequestPayload.Empty;
        }
    }

    private static string ContainerText(RequestPayload payload, RateHeader rate)
    {
        var size = payload.EquipmentSize == "—" ? string.Empty : payload.EquipmentSize;
        var type = payload.EquipmentType == "—"
            ? (rate.ContainerTypeName ?? rate.ContainerTypeCode ?? string.Empty)
            : payload.EquipmentType;
        var quantity = payload.EquipmentQuantity == "—"
            ? rate.ContainerQuantity.ToString()
            : payload.EquipmentQuantity;
        var description = string.Join(" ", new[] { size, type }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        if (string.IsNullOrWhiteSpace(description)) description = "—";
        return $"{description} · Cantidad: {quantity}";
    }

    private static string ModalityLabel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "land" or "terrestrial" or "terrestre" => "Terrestre",
        "maritime" or "sea" or "maritimo" or "marítimo" => "Marítimo",
        "air" or "aereo" or "aéreo" => "Aéreo",
        _ => string.IsNullOrWhiteSpace(value) ? "—" : value,
    };

    private static string ShipmentLabel(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "FCL" => "FCL",
        "LCL" => "LCL",
        "FTL" => "FTL",
        "LTL" => "LTL",
        _ => string.IsNullOrWhiteSpace(value) ? "—" : value.ToUpperInvariant(),
    };

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !TryGet(element, name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "Sí",
            JsonValueKind.False => "No",
            _ => null,
        };
    }

    private static string? ArrayText(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !TryGet(element, name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;
        return string.Join(", ", value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string OperationTypeLabel(RateOperationType operationType) => operationType switch
    {
        RateOperationType.Import => "Importación",
        RateOperationType.Export => "Exportación",
        RateOperationType.TransitDomestic => "Tránsito / doméstico",
        _ => operationType.ToString(),
    };

    private static string Html(string? value) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value);

    private sealed record SellerRateStatusRequest(
        string Status,
        string? Reason,
        string? IdtraNumber
    );

    private sealed record RequestPayload(
        string Modality,
        string ShipmentMode,
        string EquipmentSize,
        string EquipmentType,
        string EquipmentQuantity,
        string CargoReadyDate,
        string CargoDescription,
        string CargoValue,
        string PortHandlingMode,
        string Services
    )
    {
        public static RequestPayload Empty { get; } = new("—", "—", "—", "—", "1", "—", "—", "—", "—", "—");
    }
}

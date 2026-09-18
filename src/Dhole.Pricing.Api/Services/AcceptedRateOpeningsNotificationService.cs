using System.Net;
using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

public sealed class AcceptedRateOpeningsNotificationService(
    ICommandDispatcher dispatcher,
    IIntegrationEventOutboxWriter outbox,
    ServiceDbContext db,
    ILogger<AcceptedRateOpeningsNotificationService> logger)
{
    private const string OpeningsEmail = "aperturas@grupocastrofallas.com";
    private const string NotificationEventName = "notifications.notification.requested";

    public async Task QueueAsync(Guid rateId, CancellationToken cancellationToken = default)
    {
        try
        {
            var rate = await db.RateHeaders
                .AsNoTracking()
                .Include(x => x.RateContainers)
                .Include(x => x.RateServices)
                .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);

            if (rate is null)
            {
                logger.LogError(
                    "No se pudo preparar la solicitud de apertura porque la tarifa {RateId} no existe.",
                    rateId
                );
                return;
            }

            var request = await db.RateRequests
                .AsNoTracking()
                .Where(x => x.RateId == rateId)
                .OrderByDescending(x => x.RequestedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

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

            var payload = ReadRequestPayload(request?.PayloadJson);
            var sellerText = request?.SellerName
                ?? request?.ExecutiveName
                ?? rate.ExecutiveName
                ?? "Ventas";
            var clientText = rate.ClientName ?? request?.ClientName ?? "Cliente";
            var quoteNumber = rate.QuoNumber ?? rate.RateCode;
            var polText = string.IsNullOrWhiteSpace(rate.PolName) ? "—" : rate.PolName;
            var poeText = string.IsNullOrWhiteSpace(rate.PoeName) ? "—" : rate.PoeName;
            var podText = rate.PodName ?? request?.PodName ?? "—";
            var routeText = $"{polText} → {poeText} → {podText}";
            var incotermText = rate.IncotermName ?? rate.IncotermCode ?? "—";
            var operationTypeText = OperationTypeLabel(rate.OperationType);

            var cargoTypeSource = request?.ShipmentMode;
            if (string.IsNullOrWhiteSpace(cargoTypeSource) || cargoTypeSource == "—")
                cargoTypeSource = payload.ShipmentMode;
            if (string.IsNullOrWhiteSpace(cargoTypeSource) || cargoTypeSource == "—")
                cargoTypeSource = rate.ShipmentMode.ToString();

            var cargoTypeText = ShipmentLabel(cargoTypeSource);
            var transportTypeText = ResolveTransportType(payload.Modality, cargoTypeText);

            var equipmentLines = rate.RateContainers.Count == 0
                ? new[]
                {
                    new
                    {
                        quantity = payload.EquipmentQuantity == "—"
                            ? rate.ContainerQuantity.ToString()
                            : payload.EquipmentQuantity,
                        size = payload.EquipmentSize == "—"
                            ? EquipmentSize(rate.ContainerTypeName, rate.ContainerTypeCode)
                            : payload.EquipmentSize,
                        type = payload.EquipmentType == "—"
                            ? EquipmentType(rate.ContainerTypeName, rate.ContainerTypeCode)
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

            var servicesText = string.Join(
                ", ",
                serviceValues.Where(x => !string.IsNullOrWhiteSpace(x) && x != "—")
            );
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
                rateId
            );
        }
    }

    private static RequestPayload ReadRequestPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return RequestPayload.Empty;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var form = TryGet(root, "form", out var nestedForm)
                && nestedForm.ValueKind == JsonValueKind.Object
                    ? nestedForm
                    : root;

            var equipmentQuantity =
                Text(form, "equipmentQuantity")
                ?? Text(form, "containerQuantity")
                ?? Text(form, "quantity");

            var services =
                ArrayText(form, "serviceNames")
                ?? ArrayText(form, "services")
                ?? Text(form, "serviceName")
                ?? Text(form, "services");

            return new RequestPayload(
                Text(form, "modality") ?? "—",
                Text(form, "shipmentMode") ?? "—",
                Text(form, "equipmentSize") ?? "—",
                Text(form, "equipmentType") ?? "—",
                equipmentQuantity ?? "1",
                services ?? "—"
            );
        }
        catch (JsonException)
        {
            return RequestPayload.Empty;
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
            "AIR" => "Aéreo",
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
        "AIR" => "AIR",
        _ => string.IsNullOrWhiteSpace(value) ? "—" : value.ToUpperInvariant(),
    };

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !TryGet(element, name, out var value))
            return null;

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
        if (element.ValueKind != JsonValueKind.Object
            || !TryGet(element, name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return string.Join(
            ", ",
            value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
        );
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

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

    private static string Html(string? value)
        => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value);

    private sealed record RequestPayload(
        string Modality,
        string ShipmentMode,
        string EquipmentSize,
        string EquipmentType,
        string EquipmentQuantity,
        string Services
    )
    {
        public static RequestPayload Empty { get; } =
            new("—", "—", "—", "—", "1", "—");
    }
}

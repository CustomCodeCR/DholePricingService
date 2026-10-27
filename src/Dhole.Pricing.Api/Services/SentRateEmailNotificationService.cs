using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Prepara la notificación antes del cambio de estado. El SetRateStatusCommandHandler
/// persiste el Outbox y el estado Sent juntos en la misma unidad de trabajo.
/// </summary>
public sealed class SentRateEmailNotificationService(
    ICommandDispatcher dispatcher,
    IIntegrationEventOutboxWriter outbox,
    ServiceDbContext db,
    IPricingConfigCatalogClient configCatalog,
    AuthSellerDirectoryService authDirectory,
    IConfiguration configuration,
    ILogger<SentRateEmailNotificationService> logger)
{
    private const string NotificationEventName = "notifications.notification.requested";
    private const string NotificationType = "pricing.rate.sent-to-seller";
    private const string SalesExecutivesCatalog = "pricing-sales-executives";
    private static readonly string[] CommercialCopyRecipients =
    [
        "pricingcf@castrofallas.com",
        "jmesen@castrofallas.com",
        "cquiros@castrofallas.com",
    ];

    /// <returns>Null cuando el correo quedó preparado en el Outbox; de lo contrario, el motivo.</returns>
    public async Task<string?> QueueBeforeStatusTransitionAsync(
        Guid rateId,
        CancellationToken cancellationToken,
        string? resendCorrelationId = null)
    {
        var rate = await db.RateHeaders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);

        if (rate is null)
            return "La tarifa no existe.";

        var request = await db.RateRequests
            .AsNoTracking()
            .Where(x => x.RateId == rateId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // El ejecutivo elegido en el wizard es la fuente de verdad: no enviar
        // por defecto al creador ni a un vendedor de otra solicitud.
        var executiveName = FirstNonEmpty(rate.ExecutiveName, request?.ExecutiveName);
        if (executiveName is null)
            return "Seleccione un vendedor y guarde la tarifa antes de marcarla como Enviada.";

        var emails = await ResolveRecipientsAsync(
            executiveName,
            request?.SellerName,
            request?.SellerEmail,
            cancellationToken);

        if (emails.Count == 0)
        {
            logger.LogWarning(
                "No se encontró destinatario para el ejecutivo {ExecutiveName} de la tarifa {RateId}.",
                executiveName, rateId);
            return $"El vendedor '{executiveName}' no tiene un correo válido configurado. No se cambió el estado.";
        }

        // El vendedor seleccionado recibe un único correo y Pricing queda en
        // copia. Notifications enviaría correos independientes si añadimos
        // cada destinatario en 'recipients', por eso usamos 'payload.cc'.
        // Evitar que una dirección aparezca simultáneamente en Para y CC.
        var toRecipients = emails.Take(1).ToArray();
        var ccRecipients = CommercialCopyRecipients
            .Where(email => !toRecipients.Contains(email, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var documentResult = await dispatcher.DispatchAsync(
            new GenerateRateDocumentCommand(rate.Id, null, "pdf"),
            cancellationToken);

        if (!documentResult.IsSuccess)
        {
            logger.LogWarning("No se pudo generar el PDF para enviar la tarifa {RateId}.", rate.Id);
            return "No se pudo generar el PDF de la cotización. No se cambió el estado.";
        }

        var document = documentResult.Value;
        var quo = FirstNonEmpty(rate.QuoNumber, rate.RateCode) ?? rate.Id.ToString();
        var client = FirstNonEmpty(rate.ClientName, request?.ClientName) ?? "Cliente";
        var route = string.Join(" → ", new[] { rate.PolName, rate.PoeName, rate.PodName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        // El vendedor accede a su propia tarifa sin buscarla en el listado.
        var isCastroFallas = SameName(executiveName, "Castro Fallas");
        var rateRoute = isCastroFallas
            ? $"/pricing/rates/{rate.Id:D}/wizard?mode=view"
            : $"/pricing/seller-rates/{rate.Id:D}";
        var configuredBaseUrl = configuration["Pricing:PublicWebBaseUrl"];
        var publicBaseUrl = Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredUri)
            && configuredUri.Scheme is "https" or "http"
            ? configuredUri.GetLeftPart(UriPartial.Authority)
            : "https://sistema.logisticacastrofallas.com";
        var rateUrl = publicBaseUrl + rateRoute;
        var emailBody = $"""
            <p>Se adjunta la cotización <strong>{Html(quo)}</strong> lista para remitir al cliente.</p>
            <table style="border-collapse:collapse; width:100%; max-width:740px">
              <tr><td style="padding:5px 10px"><strong>Vendedor</strong></td><td>{Html(executiveName)}</td></tr>
              <tr><td style="padding:5px 10px"><strong>Cliente</strong></td><td>{Html(client)}</td></tr>
              <tr><td style="padding:5px 10px"><strong>Ruta</strong></td><td>{Html(route)}</td></tr>
              <tr><td style="padding:5px 10px"><strong>Modalidad</strong></td><td>{Html(rate.ShipmentMode.ToString())}</td></tr>
            </table>
            <p><a href="{Html(rateUrl)}" style="display:inline-block;padding:12px 18px;background:#eb3a22;color:#fff;text-decoration:none;border-radius:8px;font-weight:700">Abrir tarifa {Html(quo)}</a></p>
            <p>Acceso directo: <a href="{Html(rateUrl)}">{Html(rateUrl)}</a></p>
            <p>Inicie sesión en Dhole si se le solicita. El PDF comercial se incluye adjunto.</p>
            """;

        await outbox.WriteAsync(
            NotificationEventName,
            NotificationEventName,
            new
            {
                notificationType = NotificationType,
                channel = "Email",
                entityType = "RateHeader",
                entityId = rate.Id.ToString(),
                subject = $"Tarifa enviada - {quo} - {client}",
                body = emailBody,
                payload = new
                {
                    rateId = rate.Id,
                    quoNumber = quo,
                    clientName = client,
                    sellerName = executiveName,
                    action = "open-rate",
                    route = rateRoute,
                    url = rateUrl,
                    cc = ccRecipients,
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
                recipients = toRecipients.Select(email => new
                {
                    userId = (string?)null,
                    address = email,
                    displayName = executiveName,
                }).ToArray(),
            },
            correlationId: resendCorrelationId ?? $"pricing-rate-sent:{rate.Id:N}:rev:{rate.RevisionNumber}",
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Correo con PDF preparado en Outbox para tarifa {RateId}, vendedor {ExecutiveName}, destinatarios Para {ToCount}, CC {CcCount}.",
            rateId, executiveName, toRecipients.Length, ccRecipients.Length);

        return null;
    }

    internal async Task<IReadOnlyList<string>> ResolveRecipientsAsync(
        string executiveName,
        string? requestSellerName,
        string? requestSellerEmail,
        CancellationToken cancellationToken)
    {
        if (SameName(executiveName, "Castro Fallas"))
            return ["pricingcf@castrofallas.com"];

        // El selector del wizard proviene de Config y tiene email en MetadataJson.
        try
        {
            var catalog = await configCatalog.GetActiveByGroupAsync(
                SalesExecutivesCatalog, cancellationToken);
            var matching = catalog
                .Where(x => SameName(x.Name, executiveName) || SameName(x.Value, executiveName))
                .ToArray();

            if (matching.Length == 1)
            {
                var email = CatalogEmail(matching[0].MetadataJson);
                if (ValidEmail(email))
                    return [email!.Trim()];
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "No se pudo consultar el correo del ejecutivo en Config.");
        }

        // Las solicitudes iniciadas por el mismo vendedor incluyen su correo.
        if (SameName(executiveName, requestSellerName) && ValidEmail(requestSellerEmail))
            return [requestSellerEmail!.Trim()];

        // Compatibilidad con ejecutivos que ya son usuarios de Auth.
        try
        {
            var executives = await authDirectory.GetSalesExecutivesAsync(cancellationToken);
            var matches = executives
                .Where(x => SameName(x.DisplayName, executiveName)
                    || SameName(x.UserName, executiveName))
                .Where(x => ValidEmail(x.Email))
                .Select(x => x.Email!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (matches.Length == 1)
                return matches;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "No se pudo consultar el ejecutivo en Auth.");
        }

        return [];
    }

    private static string? CatalogEmail(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var item in document.RootElement.EnumerateObject())
            {
                if (item.Name.Equals("email", StringComparison.OrdinalIgnoreCase)
                    && item.Value.ValueKind == JsonValueKind.String)
                    return item.Value.GetString();
            }
        }
        catch (JsonException)
        {
            // No usar metadatos malformados como destinatarios.
        }

        return null;
    }

    private static bool ValidEmail(string? email)
        => !string.IsNullOrWhiteSpace(email)
           && MailAddress.TryCreate(email.Trim(), out var parsed)
           && string.Equals(parsed.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameName(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && Normalize(left) == Normalize(right);

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var chars = decomposed.Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return string.Join(" ", new string(chars.ToArray())
            .Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static string Html(string? value)
        => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value);
}

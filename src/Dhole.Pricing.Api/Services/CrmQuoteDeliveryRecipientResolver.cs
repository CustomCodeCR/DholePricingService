using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text.Json;

namespace Dhole.Pricing.Api.Services;

// Never trust an email supplied by the browser: resolve the selected contact
// and the account's actual assigned seller from CRM on every dispatch.
public sealed record CrmQuoteDeliveryRecipient(
    string AssignedSellerName, string? ContactName, string? ContactEmail);

public sealed class CrmQuoteDeliveryRecipientResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<CrmQuoteDeliveryRecipientResolver> logger,
    IConfiguration configuration)
{
    public sealed record AccountDelivery(
        string DeliveryTarget, Guid? ContactId, string? ContactName,
        string? ContactEmail, string SellerName, string SellerEmail);

    // The current CRM account preference is authoritative for every later QUO.
    // Service-key authentication also works for retry workers without a browser JWT.
    public async Task<(AccountDelivery? Delivery, string? Error)> ReadAccountDeliveryAsync(
        Guid accountId, CancellationToken ct)
    {
        if (accountId == Guid.Empty)
            return (null, "La cotización no tiene una cuenta CRM vinculada.");
        var secret = configuration["Crm:Client:InternalServiceKey"]
            ?? configuration["Auth:Client:InternalServiceKey"]
            ?? configuration["INTERNAL_SERVICE_KEY"];
        if (string.IsNullOrWhiteSpace(secret))
            return (null, "No está configurada la comunicación interna segura con CRM.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"api/internal/crm/accounts/{accountId:D}/quote-delivery");
            request.Headers.TryAddWithoutValidation("X-Dhole-Service-Key", secret);
            using var response = await httpClientFactory.CreateClient("DholeCrmQuoteDelivery")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CRM rejected account delivery policy for {AccountId} ({Status})",
                    accountId, (int)response.StatusCode);
                return (null, "CRM no pudo validar la preferencia de envío, el contacto o el vendedor.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var data = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var json = data.RootElement;
            var target = ReadString(json, "deliveryTarget");
            var sellerEmail = ReadString(json, "sellerEmail")?.Trim();
            var sellerName = ReadString(json, "sellerName");
            var contactEmail = ReadString(json, "contactEmail")?.Trim();
            var id = Guid.TryParse(ReadString(json, "contactId"), out var parsed) ? parsed : (Guid?)null;
            if (target is not ("seller" or "client") ||
                string.IsNullOrWhiteSpace(sellerName) ||
                string.IsNullOrWhiteSpace(sellerEmail) ||
                !MailAddress.TryCreate(sellerEmail, out var sellerAddress) ||
                !string.Equals(sellerAddress.Address, sellerEmail, StringComparison.OrdinalIgnoreCase))
                return (null, "El vendedor de la cuenta no tiene correo válido.");
            if (target == "client" && (id is null || string.IsNullOrWhiteSpace(contactEmail) ||
                !MailAddress.TryCreate(contactEmail, out var contactAddress) ||
                !string.Equals(contactAddress.Address, contactEmail, StringComparison.OrdinalIgnoreCase)))
                return (null, "El contacto seleccionado en la cuenta ya no tiene correo válido.");

            return (new AccountDelivery(target, id, ReadString(json, "contactName"),
                contactEmail, sellerName, sellerEmail), null);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Could not resolve CRM quote delivery policy for {AccountId}", accountId);
            return (null, "No fue posible consultar la preferencia de envío de la cuenta.");
        }
    }

    public async Task<(CrmQuoteDeliveryRecipient? Recipient, string? Error)> ResolveAsync(
        Guid accountId, Guid? contactId, string? authorization, CancellationToken ct)
    {
        if (accountId == Guid.Empty)
            return (null, "La cotización debe estar vinculada a una cuenta CRM.");
        if (string.IsNullOrWhiteSpace(authorization) ||
            !AuthenticationHeaderValue.TryParse(authorization, out var bearer) ||
            !string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(bearer.Parameter))
            return (null, "Debe iniciar sesión para consultar los contactos de la cuenta.");

        // CRM enforces account visibility using the current caller's JWT.
        using var seller = await ReadCrmAsync(
            $"api/crm/accounts/{accountId:D}/assigned-executive", authorization, ct);
        if (seller is null)
            return (null, "No fue posible verificar el vendedor asignado y los permisos de la cuenta en CRM.");
        var sellerName = ReadString(seller.RootElement, "displayName");
        if (string.IsNullOrWhiteSpace(sellerName))
            return (null, "La cuenta no tiene un vendedor asignado válido.");

        if (contactId is null)
            return (new CrmQuoteDeliveryRecipient(sellerName, null, null), null);

        if (contactId == Guid.Empty)
            return (null, "Seleccione un contacto válido de la cuenta.");

        using var contacts = await ReadCrmAsync(
            $"api/crm/accounts/{accountId:D}/contacts", authorization, ct);
        if (contacts is null || contacts.RootElement.ValueKind != JsonValueKind.Array)
            return (null, "No fue posible validar los contactos vinculados a la cuenta en CRM.");

        foreach (var relation in contacts.RootElement.EnumerateArray())
        {
            var contact = relation.TryGetProperty("contact", out var nested) ? nested : relation;
            if (contact.ValueKind != JsonValueKind.Object ||
                !Guid.TryParse(ReadString(contact, "id"), out var id) || id != contactId)
                continue;

            var email = ReadString(contact, "email")?.Trim();
            if (string.IsNullOrWhiteSpace(email) ||
                !MailAddress.TryCreate(email, out var parsed) ||
                !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
                return (null, "El contacto seleccionado no tiene un correo electrónico válido.");

            return (new CrmQuoteDeliveryRecipient(
                sellerName, ReadString(contact, "name") ?? "Contacto", email), null);
        }

        return (null, "El contacto seleccionado ya no está vinculado a esta cuenta.");
    }

    private async Task<JsonDocument?> ReadCrmAsync(string path, string authorization, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            using var response = await httpClientFactory.CreateClient("DholeCrmQuoteDelivery")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CRM rejected quote recipient verification: {Path} ({StatusCode})",
                    path, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "CRM quote recipient verification unavailable.");
            return null;
        }
    }

    private static string? ReadString(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object &&
           value.TryGetProperty(key, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString() : null;
}

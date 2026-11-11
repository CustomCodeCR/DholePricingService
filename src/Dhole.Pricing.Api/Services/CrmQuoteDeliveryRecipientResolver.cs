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
    ILogger<CrmQuoteDeliveryRecipientResolver> logger)
{
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

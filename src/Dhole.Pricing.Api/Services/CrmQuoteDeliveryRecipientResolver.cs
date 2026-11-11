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
    // Account-level delivery choice is the only source of truth for future quotes.
    // Fail closed on a CRM outage rather than silently sending to the wrong party.
    public async Task<(string? DeliveryTarget, Guid? ContactId, string? Error)>
        ResolvePreferenceAsync(Guid accountId, string? authorization, CancellationToken ct)
    {
        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(authorization) ||
            !AuthenticationHeaderValue.TryParse(authorization, out var bearer) ||
            !string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(bearer.Parameter))
            return (null, null, "No fue posible verificar la configuración de envío de la cuenta.");

        using var preference = await ReadCrmAsync(
            $"api/crm/accounts/{accountId:D}/quote-delivery-preference", authorization, ct);
        if (preference is null)
            return (null, null, "CRM no está disponible para consultar la configuración de envío.");

        var target = ReadString(preference.RootElement, "deliveryTarget");
        if (target == "seller") return ("seller", null, null);
        if (target == "client" &&
            Guid.TryParse(ReadString(preference.RootElement, "contactId"), out var contactId) &&
            contactId != Guid.Empty)
            return ("client", contactId, null);

        return (null, null, "La configuración de envío del cliente no es válida; corríjala en CRM.");
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


    public sealed record ContactRecipient(Guid Id, string Name, string Email);
    public sealed record VerifiedRecipients(string AssignedSellerName, IReadOnlyList<ContactRecipient> Contacts);

    // Always derive the recipients from the authenticated account preference,
    // never from client-supplied email addresses.
    public async Task<(string? DeliveryTarget, Guid[] ContactIds, string? Error)>
        ResolveMultiPreferenceAsync(Guid accountId, string? authorization, CancellationToken ct)
    {
        if (accountId == Guid.Empty || !IsBearer(authorization))
            return (null, [], "No fue posible verificar la configuración de envío de la cuenta.");
        using var preference = await ReadCrmAsync(
            $"api/crm/accounts/{accountId:D}/quote-delivery-preference", authorization!, ct);
        if (preference is null)
            return (null, [], "CRM no está disponible para consultar la configuración de envío.");
        var target = ReadString(preference.RootElement, "deliveryTarget");
        if (target == "seller") return ("seller", [], null);
        if (target != "client")
            return (null, [], "La configuración de envío de la cuenta es inválida.");
        var ids = new List<Guid>();
        if (preference.RootElement.TryGetProperty("contactIds", out var list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in list.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(value.GetString(), out var id) || id == Guid.Empty)
                    return (null, [], "La configuración contiene contactos inválidos.");
                ids.Add(id);
            }
        }
        if (ids.Count == 0 &&
            Guid.TryParse(ReadString(preference.RootElement, "contactId"), out var legacy) &&
            legacy != Guid.Empty)
            ids.Add(legacy);
        if (ids.Count is < 1 or > 100 || ids.Distinct().Count() != ids.Count)
            return (null, [], "Configure uno o varios contactos válidos en CRM.");
        return ("client", ids.ToArray(), null);
    }

    public async Task<(VerifiedRecipients? Recipients, string? Error)> ResolveManyAsync(
        Guid accountId, IReadOnlyList<Guid> contactIds, string? authorization, CancellationToken ct)
    {
        if (accountId == Guid.Empty || !IsBearer(authorization))
            return (null, "No fue posible verificar los permisos de la cuenta.");
        if (contactIds.Count > 100 || contactIds.Contains(Guid.Empty) ||
            contactIds.Distinct().Count() != contactIds.Count)
            return (null, "La configuración de destinatarios contiene contactos inválidos.");

        using var seller = await ReadCrmAsync(
            $"api/crm/accounts/{accountId:D}/assigned-executive", authorization!, ct);
        if (seller is null)
            return (null, "No fue posible verificar el vendedor y los permisos de la cuenta.");
        var name = ReadString(seller.RootElement, "displayName");
        if (string.IsNullOrWhiteSpace(name))
            return (null, "La cuenta no tiene un vendedor válido.");
        if (contactIds.Count == 0)
            return (new VerifiedRecipients(name, []), null);

        using var contacts = await ReadCrmAsync(
            $"api/crm/accounts/{accountId:D}/contacts", authorization!, ct);
        if (contacts is null || contacts.RootElement.ValueKind != JsonValueKind.Array)
            return (null, "No se pudieron verificar los contactos de la cuenta.");
        var requested = contactIds.ToHashSet();
        var resolved = new Dictionary<Guid, ContactRecipient>();
        foreach (var relation in contacts.RootElement.EnumerateArray())
        {
            var contact = relation.TryGetProperty("contact", out var nested) ? nested : relation;
            if (contact.ValueKind != JsonValueKind.Object ||
                !Guid.TryParse(ReadString(contact, "id"), out var id) ||
                !requested.Contains(id)) continue;
            var status = ReadString(contact, "status");
            var email = ReadString(contact, "email")?.Trim();
            if (status is not null &&
                !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
                return (null, "Un contacto seleccionado ya no está activo.");
            if (string.IsNullOrWhiteSpace(email) ||
                !MailAddress.TryCreate(email, out var parsed) ||
                !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
                return (null, "Un contacto seleccionado ya no tiene un correo electrónico válido.");
            resolved[id] = new ContactRecipient(id, ReadString(contact, "name") ?? "Contacto", email);
        }
        if (resolved.Count != requested.Count)
            return (null, "Uno o varios contactos ya no pertenecen a la cuenta.");
        return (new VerifiedRecipients(name, contactIds.Select(id => resolved[id]).ToArray()), null);
    }

    private static bool IsBearer(string? authorization) =>
        !string.IsNullOrWhiteSpace(authorization) &&
        AuthenticationHeaderValue.TryParse(authorization, out var bearer) &&
        string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(bearer.Parameter);

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

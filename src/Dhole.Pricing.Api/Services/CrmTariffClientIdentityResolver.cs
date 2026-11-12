using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Resolves an account or lead from the exact permission-filtered CRM picker
/// available to the authenticated caller. Never trust browser-supplied names or
/// seller identity when applying a master tariff.
/// </summary>
public sealed record VerifiedCrmTariffClient(Guid Id, string Kind, string DisplayName, string ExecutiveName);

public sealed class CrmTariffClientIdentityResolver(
    IHttpClientFactory clientFactory,
    ILogger<CrmTariffClientIdentityResolver> logger)
{
    public async Task<(VerifiedCrmTariffClient? Client, string? Error)> ResolveAsync(
        Guid clientId, string kind, string? authorization, CancellationToken ct)
    {
        if (clientId == Guid.Empty || (kind != "account" && kind != "lead"))
            return (null, "Seleccione una cuenta o prospecto válido en CRM.");
        if (string.IsNullOrWhiteSpace(authorization) ||
            !AuthenticationHeaderValue.TryParse(authorization, out var bearer) ||
            !string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(bearer.Parameter))
            return (null, "Se requiere una sesión activa para validar el cliente en CRM.");

        var path = $"api/crm/pricing-client-options?mode=create&selectedClientId={clientId:D}&pageSize=1";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = bearer;
            using var response = await clientFactory.CreateClient("DholeCrmQuoteDelivery")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CRM tariff client verification rejected: {StatusCode}",
                    (int)response.StatusCode);
                return (null, response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                    ? "No tiene permiso para utilizar esta cuenta o prospecto de CRM."
                    : "No fue posible verificar el cliente en CRM. No se creó ninguna QUO.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return (null, "CRM devolvió una respuesta de clientes inválida.");
            foreach (var row in items.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object ||
                    !Guid.TryParse(ReadString(row, "id"), out var id) || id != clientId ||
                    !string.Equals(ReadString(row, "kind"), kind, StringComparison.OrdinalIgnoreCase))
                    continue;
                var name = ReadString(row, "displayName")?.Trim();
                var institutional = row.TryGetProperty("isCastroFallasExecutive", out var flag) &&
                    flag.ValueKind == JsonValueKind.True;
                var seller = institutional ? "Castro Fallas" : ReadString(row, "executiveName")?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 220 ||
                    string.IsNullOrWhiteSpace(seller) || seller.Length > 220 ||
                    string.Equals(seller, "Vendedor no disponible", StringComparison.OrdinalIgnoreCase))
                    return (null, "La cuenta o prospecto no tiene un ejecutivo comercial válido en CRM.");
                return (new VerifiedCrmTariffClient(id, kind, name, seller), null);
            }
            return (null, "La cuenta o prospecto seleccionado ya no está disponible para este usuario.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(error, "CRM tariff identity lookup failed");
            return (null, "CRM no está disponible para verificar la identidad comercial.");
        }
    }

    private static string? ReadString(JsonElement row, string key) =>
        row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}

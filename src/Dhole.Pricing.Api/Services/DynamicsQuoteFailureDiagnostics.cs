using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Non-secret, machine-readable error descriptions for Pricing operations.
/// Request payloads, API keys, connection strings and remote stack traces
/// are never persisted or sent to the frontend.
/// </summary>
public static class DynamicsQuoteFailureDiagnostics
{
    private static readonly Regex Secret = new(
        @"(?i)\b(api[-_ ]?key|password|authorization|bearer|token|secret|connectionstring)\b\s*[:=]\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Reject codes raised before the Dataverse Quote write; never treat
    // ambiguous network outcomes or partially created Quotes as safe retries.
    public static bool IsConfirmedPreWriteFailure(string? providerCode) =>
        providerCode is "quote_total_field_not_authorized" or
            "quote_id_read_not_authorized" or "quote_detail_relationship_missing" or
            "quote_lookup_target_not_authorized" or
            "quote_lookup_name_field_not_authorized" or "write_grant_denied" or
            "client_denied" or "scope_denied" or "lookup_target_denied" or
            "lookup_target_fields_denied" or "quote_customer_required" or
            "table_denied" or "field_denied" or "crud_template_inactive" or
            "crud_template_scope_denied" or "template_mapping_field_denied" or
            "quote_creation_key_required" or "crud_key_invalid" or
            "production_writes_not_approved" or "production_quote_writes_not_approved" or
            "lookup_name_not_found" or "lookup_name_ambiguous" or
            "lookup_name_field_not_authorized" or "lookup_relation_not_approved" or
            "preview_lookup_target_denied" or "choice_label_not_found" or
            "choice_label_ambiguous" or "quote_text_exceeds_dataverse_limit";

    // A definitive policy rejection happens in the HTTP middleware before
    // template execution and BEFORE any Dataverse write or idempotency claim.
    // For migrated records, this exact code + HTTP 403 is proof of safety
    // even if the old worker recorded postAttempted=true.
    public static bool IsConfirmedPolicyDenial(JsonElement diagnostic) =>
        diagnostic.ValueKind == JsonValueKind.Object &&
        diagnostic.TryGetProperty("httpStatus", out var http) &&
        http.ValueKind == JsonValueKind.Number && http.TryGetInt32(out var code) &&
        code == 403 &&
        diagnostic.TryGetProperty("stage", out var stage) &&
        stage.ValueKind == JsonValueKind.String &&
        stage.GetString() == "execute" &&
        diagnostic.TryGetProperty("providerCode", out var provider) &&
        provider.ValueKind == JsonValueKind.String &&
        provider.GetString() is ("production_writes_not_approved" or
            "production_quote_writes_not_approved");

    /// <summary>
    /// The old worker incorrectly marked POST-attempted for a connector
    /// validation HTTP 422. This precise provider code can ONLY be emitted
    /// from PricingQuoteTextLimits before any header write or ledger entry.
    /// Do not generalize this override to other 422/409/unknown errors.
    /// </summary>
    public static bool IsKnownLegacyTextPreflight(string? diagnosticJson)
    {
        if (string.IsNullOrWhiteSpace(diagnosticJson)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(diagnosticJson);
            var root = parsed.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("stage", out var stage) &&
                stage.ValueKind == JsonValueKind.String &&
                stage.GetString() == "execute" &&
                root.TryGetProperty("providerCode", out var code) &&
                code.ValueKind == JsonValueKind.String &&
                code.GetString() == "quote_text_exceeds_dataverse_limit" &&
                root.TryGetProperty("httpStatus", out var status) &&
                status.ValueKind == JsonValueKind.Number &&
                status.TryGetInt32(out var http) && http == 422 &&
                root.TryGetProperty("postAttempted", out var attempt) &&
                attempt.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Earlier Pricing workers marked a connector ReadAccess client_denied
    /// as postAttempted even though that 403 is raised by PermitAsync before
    /// executing the Quote header. Connector paths AFTER a successful header
    /// return quote_total_partial_requires_reconciliation instead.
    /// This permits reusing the SAME idempotency key, never creating a new
    /// recovery generation for a missing read grant.
    /// </summary>
    public static bool IsKnownLegacyReadGrantPreflight(string? diagnosticJson)
    {
        if (string.IsNullOrWhiteSpace(diagnosticJson)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(diagnosticJson);
            var value = parsed.RootElement;
            return value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("stage", out var stage) &&
                stage.ValueKind == JsonValueKind.String &&
                stage.GetString() == "execute" &&
                value.TryGetProperty("providerCode", out var code) &&
                code.ValueKind == JsonValueKind.String &&
                code.GetString() == "client_denied" &&
                value.TryGetProperty("httpStatus", out var status) &&
                status.ValueKind == JsonValueKind.Number &&
                status.TryGetInt32(out var http) && http == 403;
        }
        catch (JsonException) { return false; }
    }

    public static (string LastError, string DiagnosticJson) Format(
        string stage, Exception exception, bool postAttempted,
        HttpStatusCode? httpStatus = null, string? providerCode = null, string? providerTitle = null,
        string? lookupInput = null, string? lookupSearchField = null)
    {
        var code = exception switch
        {
            InvalidOperationException when stage == "mapping" => "DYNAMICS_MAPPING_INVALID",
            InvalidOperationException when stage == "currency" => "DYNAMICS_CURRENCY_INVALID",
            InvalidOperationException when stage == "schema" => "DYNAMICS_SCHEMA_INVALID",
            JsonException => "DYNAMICS_RESPONSE_INVALID",
            HttpRequestException or TaskCanceledException => "DYNAMICS_NETWORK_ERROR",
            _ when httpStatus == HttpStatusCode.Unauthorized => "DYNAMICS_UNAUTHORIZED",
            _ when httpStatus == HttpStatusCode.Forbidden => "DYNAMICS_FORBIDDEN",
            _ when httpStatus == HttpStatusCode.NotFound => "DYNAMICS_NOT_FOUND",
            _ when httpStatus.HasValue => "DYNAMICS_HTTP_ERROR",
            _ => "DYNAMICS_DELIVERY_FAILED"
        };

        // Only our local mapping exceptions and explicitly selected provider
        // error fields may contribute text. Never record arbitrary exception
        // content such as a failed request's URI or an HTTP response body.
        var description = exception is InvalidOperationException
            ? Safe(exception.Message, 320)
            : !string.IsNullOrWhiteSpace(providerTitle)
                ? Safe(providerTitle, 320)
                : code switch
                {
                    "DYNAMICS_NETWORK_ERROR" => "Timeout o error de conexión con Dynamics.",
                    "DYNAMICS_RESPONSE_INVALID" => "La respuesta del conector no es JSON válido.",
                    _ => "La operación no pudo confirmarse."
                };
        // A Dataverse lookup failure is actionable only when the name of the
        // relationship and its verified search column are visible to operators.
        // Never persist the customer's name, IDTRA value, or raw HTTP body.
        lookupInput = SafeIdentifier(lookupInput);
        lookupSearchField = SafeIdentifier(lookupSearchField);
        if (providerCode == "lookup_name_not_found" && lookupInput is not null)
            description = $"Dynamics no encontró un registro para {lookupInput}" +
                (lookupSearchField is null ? "." : $" en el campo {lookupSearchField}.") +
                " Revisá el registro relacionado y el mapeo antes de reintentar.";
        else if (providerCode == "lookup_name_ambiguous" && lookupInput is not null)
            description = $"Dynamics encontró varios registros para {lookupInput}" +
                (lookupSearchField is null ? "." : $" en el campo {lookupSearchField}.") +
                " Se necesita una coincidencia única.";
        else if (providerCode == "quote_customer_required")
            description = "La cabecera no puede crearse sin un cliente de Dynamics vinculado: " +
                "configurá customerid_account o customerid_contact en crear_oferta y " +
                "verificá que el nombre del cliente resuelva a una cuenta o contacto único. " +
                "No se hizo ningún POST de cabecera.";
        else if (providerCode == "quote_text_exceeds_dataverse_limit")
            description = lookupInput is null
                ? "Algún texto supera el límite de Dataverse. La QUO no se envió; revisá el esquema actualizado."
                : $"El campo {lookupInput} supera la longitud máxima autorizada (MaxLength) en Dataverse. " +
                  "La QUO no se envió. El sistema solo abrevia etiquetas de presentación; " +
                  "las condiciones, el cliente y el IDTRA se conservan completos.";
        var display = Safe($"[{code}] {stage}: {description}", 500);
        var json = JsonSerializer.Serialize(new
        {
            code, stage, message = description,
            httpStatus = (int?)httpStatus,
            providerCode = Safe(providerCode, 100),
            lookupInput, lookupSearchField,
            postAttempted,
            action = postAttempted
                ? "Revisar/conciliar la operación en Dynamics antes de reintentar."
                : "Corregir la plantilla, el mapeo o la configuración; después se puede reintentar.",
            recordedAtUtc = DateTime.UtcNow
        });
        return (display, json);
    }

    public static string? Safe(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var masked = Secret.Replace(text, "$1=[REDACTED]");
        var normalized = new string(masked.Where(c => !char.IsControl(c) || c == ' ').ToArray())
            .Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static string? SafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Regex.IsMatch(value, @"^[A-Za-z][A-Za-z0-9_]{0,79}$",
            RegexOptions.CultureInvariant) ? value : null;

    public static (string? LookupInput, string? LookupSearchField) ReadProviderLookup(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8192)
            return (null, null);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);
            var root = document.RootElement;
            var error = root.TryGetProperty("error", out var nested) &&
                nested.ValueKind == JsonValueKind.Object ? nested : root;
            static string? Field(JsonElement obj, string property) =>
                obj.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return (SafeIdentifier(Field(error, "lookupInput") ?? Field(root, "lookupInput")),
                SafeIdentifier(Field(error, "lookupSearchField") ??
                    Field(root, "lookupSearchField")));
        }
        catch (JsonException) { return (null, null); }
    }

    public static (string? Code, string? Title) ReadProviderError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8192) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return (null, null);
            var root = document.RootElement;
            var error = root.TryGetProperty("error", out var nested)
                        && nested.ValueKind == JsonValueKind.Object ? nested : root;

            static string? Read(JsonElement element, string name) =>
                element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var code = Safe(Read(error, "code") ?? Read(root, "code"), 100);
            var title = Safe(Read(error, "title") ?? Read(root, "title"), 320);
            // execute-with-total returns this strictly typed state for an
            // uncertain header or quotedetail write, without an error.code.
            // Preserve its stage instead of displaying only generic HTTP 409.
            if (code is null && Read(root, "state") == "requires_reconciliation")
            {
                var stage = Read(root, "stage");
                return stage switch
                {
                    "quote" => ("quote_header_requires_reconciliation",
                        "La creación de la cabecera de la QUO no se pudo confirmar. Revisá el registro de idempotencia antes de repetirla."),
                    "total" => ("quote_total_requires_reconciliation",
                        "La cabecera de la QUO se creó, pero la línea de venta total no quedó confirmada. Conciliá la línea antes de cualquier reintento."),
                    _ => ("quote_requires_reconciliation",
                        "La operación de QUO no quedó confirmada. Verificá cabecera y línea en Dynamics antes de reintentar.")
                };
            }
            return (code, title);
        }
        catch (JsonException) { return (null, null); }
    }
}

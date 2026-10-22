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
            "table_denied" or "field_denied" or "crud_template_inactive" or
            "crud_template_scope_denied" or "template_mapping_field_denied" or
            "quote_creation_key_required" or "crud_key_invalid" or
            "production_writes_not_approved" or "production_quote_writes_not_approved" or
            "lookup_name_not_found" or "lookup_name_ambiguous" or
            "lookup_name_field_not_authorized" or "lookup_relation_not_approved" or
            "preview_lookup_target_denied" or "choice_label_not_found" or
            "choice_label_ambiguous";

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

    public static (string LastError, string DiagnosticJson) Format(
        string stage, Exception exception, bool postAttempted,
        HttpStatusCode? httpStatus = null, string? providerCode = null, string? providerTitle = null)
    {
        var code = exception switch
        {
            InvalidOperationException when stage == "mapping" => "DYNAMICS_MAPPING_INVALID",
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
        var display = Safe($"[{code}] {stage}: {description}", 500);
        var json = JsonSerializer.Serialize(new
        {
            code, stage, message = description,
            httpStatus = (int?)httpStatus,
            providerCode = Safe(providerCode, 100),
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
            return (Safe(Read(error, "code") ?? Read(root, "code"), 100),
                    Safe(Read(error, "title") ?? Read(root, "title"), 320));
        }
        catch (JsonException) { return (null, null); }
    }
}

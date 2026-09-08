using System.Text.Json;

namespace Dhole.Pricing.Api.Middleware;

/// <summary>
/// Validaciones de negocio que deben cumplirse aunque el cliente Web sea omitido o esté desactualizado.
/// </summary>
public sealed class RateRequestSubmissionGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/pricing/rate-requests", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        JsonDocument? document = null;
        try
        {
            document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            context.Request.Body.Position = 0;
            await next(context);
            return;
        }
        finally
        {
            context.Request.Body.Position = 0;
        }

        using (document)
        {
            var root = document.RootElement;
            var clientName = GetStringIgnoreCase(root, "clientName");
            if (string.IsNullOrWhiteSpace(clientName))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "Pricing.RateRequestClientRequired",
                    message = "El cliente es obligatorio para solicitar una tarifa.",
                }, context.RequestAborted);
                return;
            }

            if (TryGetPropertyIgnoreCase(root, "payload", out var payload)
                && IsDangerousCargo(payload)
                && !HasTechnicalSheet(payload))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "Pricing.DangerousCargoTechnicalSheetRequired",
                    message = "Para carga peligrosa es obligatorio adjuntar la ficha técnica.",
                }, context.RequestAborted);
                return;
            }
        }

        await next(context);
    }

    private static bool IsDangerousCargo(JsonElement element)
    {
        foreach (var property in EnumerateRecursive(element))
        {
            var key = Normalize(property.Name);
            if (key is "isdangerouscargo" or "dangerouscargo" or "hazardouscargo" or "ispeligrosa" or "cargapeligrosa")
            {
                if (property.Value.ValueKind == JsonValueKind.True) return true;
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = Normalize(property.Value.GetString() ?? string.Empty);
                    if (value is "true" or "si" or "yes" or "dangerous" or "hazardous" or "peligrosa") return true;
                }
            }

            if (key is "cargotype" or "cargotypecode" or "nature" or "cargonature" or "tipocarga")
            {
                var value = Normalize(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : string.Empty);
                if (value.Contains("danger") || value.Contains("hazard") || value.Contains("peligros")) return true;
            }
        }
        return false;
    }

    private static bool HasTechnicalSheet(JsonElement element)
    {
        foreach (var property in EnumerateRecursive(element))
        {
            var key = Normalize(property.Name);
            if (key.Contains("technicalsheet") || key.Contains("fichatecnica") || key is "msds" or "sds" or "sdsfile" or "msdsfile")
            {
                if (HasValue(property.Value)) return true;
            }

            if (key is "attachments" or "files" or "documents" or "adjuntos" or "documentos")
            {
                if (ContainsTechnicalSheetAttachment(property.Value)) return true;
            }
        }
        return false;
    }

    private static bool ContainsTechnicalSheetAttachment(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = Normalize(item.GetString() ?? string.Empty);
                if (text.Contains("fichatecnica") || text.Contains("technicalsheet") || text.Contains("msds") || text.Contains("sds")) return true;
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object) continue;
            var descriptor = string.Join(' ', item.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .Select(p => p.Value.GetString()));
            var normalized = Normalize(descriptor);
            if (normalized.Contains("fichatecnica") || normalized.Contains("technicalsheet") || normalized.Contains("msds") || normalized.Contains("sds")) return true;
        }
        return false;
    }

    private static bool HasValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        JsonValueKind.True => true,
        _ => false,
    };

    private static IEnumerable<JsonProperty> EnumerateRecursive(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property;
                foreach (var nested in EnumerateRecursive(property.Value)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var nested in EnumerateRecursive(item)) yield return nested;
        }
    }

    private static string? GetStringIgnoreCase(JsonElement element, string name)
        => TryGetPropertyIgnoreCase(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
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

    private static string Normalize(string value)
        => new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

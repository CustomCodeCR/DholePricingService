using System.Text.Json;
using Dhole.Pricing.Api.Services;

namespace Dhole.Pricing.Api.Middleware;

public sealed class RateCommercialIdentityGuardMiddleware(RequestDelegate next)
{
    private const string RatesPath = "/api/pricing/rates";
    private const string DefaultSalesExecutiveName = "Castro Fallas";

    public async Task InvokeAsync(
        HttpContext context,
        AuthSellerDirectoryService sellerDirectory)
    {
        if (!IsRateWriteRequest(context.Request))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: context.RequestAborted
            );
            context.Request.Body.Position = 0;
        }
        catch (JsonException)
        {
            context.Request.Body.Position = 0;
            await next(context);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            var clientName = GetString(root, "clientName");
            if (string.IsNullOrWhiteSpace(clientName))
            {
                await WriteBadRequestAsync(
                    context,
                    "Pricing.RateClientRequired",
                    "El nombre del cliente es obligatorio para crear o guardar una tarifa."
                );
                return;
            }

            var executiveName = GetString(root, "executiveName")?.Trim();
            if (string.IsNullOrWhiteSpace(executiveName))
            {
                await WriteBadRequestAsync(
                    context,
                    "Pricing.RateExecutiveRequired",
                    "Debe seleccionar un ejecutivo comercial."
                );
                return;
            }

            if (!IsDefaultSalesExecutive(executiveName))
            {
                var executiveUserIdText = GetString(root, "executiveUserId");
                if (!Guid.TryParse(executiveUserIdText, out var executiveUserId)
                    || executiveUserId == Guid.Empty)
                {
                    await WriteBadRequestAsync(
                        context,
                        "Pricing.RateExecutiveRequired",
                        "Debe seleccionar un ejecutivo comercial."
                    );
                    return;
                }

                SellerDirectoryUser? executive;
                try
                {
                    executive = await sellerDirectory.GetSalesExecutiveAsync(
                        executiveUserId,
                        context.RequestAborted
                    );
                }
                catch (Exception exception) when (
                    exception is HttpRequestException
                    or InvalidOperationException
                    or TaskCanceledException)
                {
                    if (context.RequestAborted.IsCancellationRequested)
                        throw;

                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(
                        new
                        {
                            code = "Pricing.SalesExecutiveDirectoryUnavailable",
                            message = "No fue posible validar el ejecutivo comercial con Auth en este momento."
                        },
                        cancellationToken: context.RequestAborted
                    );
                    return;
                }

                if (executive is null)
                {
                    await WriteBadRequestAsync(
                        context,
                        "Pricing.RateExecutiveInvalid",
                        "El ejecutivo comercial debe ser un usuario activo con el rol Vendedor."
                    );
                    return;
                }

                var canonicalExecutiveName = GetExecutiveLabel(executive);
                if (!string.Equals(
                        executiveName,
                        canonicalExecutiveName,
                        StringComparison.Ordinal))
                {
                    await WriteBadRequestAsync(
                        context,
                        "Pricing.RateExecutiveNameMismatch",
                        "El nombre del ejecutivo comercial no coincide con el usuario Vendedor seleccionado."
                    );
                    return;
                }
            }
        }

        await next(context);
    }

    private static bool IsRateWriteRequest(HttpRequest request)
    {
        var path = request.Path.Value?.TrimEnd('/') ?? string.Empty;
        if (HttpMethods.IsPost(request.Method))
            return path.Equals(RatesPath, StringComparison.OrdinalIgnoreCase);

        if (!HttpMethods.IsPut(request.Method)
            || !path.StartsWith(RatesPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rateId = path[(RatesPath.Length + 1)..];
        return !rateId.Contains('/') && Guid.TryParse(rateId, out _);
    }

    private static bool IsDefaultSalesExecutive(string executiveName)
        => string.Equals(
            executiveName.Trim(),
            DefaultSalesExecutiveName,
            StringComparison.OrdinalIgnoreCase
        );

    private static string? GetString(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => null,
                _ => property.Value.ToString(),
            };
        }

        return null;
    }

    private static string GetExecutiveLabel(SellerDirectoryUser executive)
    {
        if (!string.IsNullOrWhiteSpace(executive.DisplayName))
            return executive.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(executive.UserName))
            return executive.UserName.Trim();
        if (!string.IsNullOrWhiteSpace(executive.Email))
            return executive.Email.Trim();
        return executive.UserId.ToString();
    }

    private static Task WriteBadRequestAsync(
        HttpContext context,
        string code,
        string message)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(
            new { code, message },
            cancellationToken: context.RequestAborted
        );
    }
}

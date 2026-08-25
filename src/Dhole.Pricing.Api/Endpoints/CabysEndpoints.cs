using System.Text.Json;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Api.Endpoints;

public static class CabysEndpoints
{
    private const string CabysBaseUrl = "https://api.hacienda.go.cr/fe/cabys";

    public static IEndpointRouteBuilder MapCabysEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/cabys", SearchCabysAsync)
            .WithTags("Pricing CABYS")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);

        return app;
    }

    private static async Task<IResult> SearchCabysAsync(
        string? q,
        string? codigo,
        int? top,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var hasCode = !string.IsNullOrWhiteSpace(codigo);
        var search = q?.Trim();

        if (!hasCode && (string.IsNullOrWhiteSpace(search) || search.Length < 3))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.CabysSearchTooShort",
                message = "Digite al menos 3 caracteres para buscar CABYS.",
            });
        }

        var uri = hasCode
            ? $"{CabysBaseUrl}?codigo={Uri.EscapeDataString(codigo!.Trim())}"
            : $"{CabysBaseUrl}?q={Uri.EscapeDataString(search!)}&top={Math.Clamp(top ?? 20, 1, 50)}";

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DholePricingService/1.0");

            using var response = await client.GetAsync(uri, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Results.Json(
                    new
                    {
                        code = "Pricing.CabysUnavailable",
                        message = "Hacienda no pudo responder la consulta CABYS.",
                        status = (int)response.StatusCode,
                    },
                    statusCode: StatusCodes.Status502BadGateway
                );
            }

            using var document = JsonDocument.Parse(content);
            return Results.Json(document.RootElement.Clone());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Results.Json(
                new
                {
                    code = "Pricing.CabysUnavailable",
                    message = "No fue posible consultar el catálogo CABYS de Hacienda.",
                },
                statusCode: StatusCodes.Status502BadGateway
            );
        }
    }
}

using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Imports.GetPanamaContinuationRates;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Api.Endpoints;

public static class PanamaContinuationRateEndpoints
{
    public static IEndpointRouteBuilder MapPanamaContinuationRateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/import-rates/panama-continuation", GetPanamaContinuationRatesAsync)
            .WithTags("Imported FCL Rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);

        return app;
    }

    private static async Task<IResult> GetPanamaContinuationRatesAsync(
        string panamaPol,
        string finalDestination,
        string? containerType,
        DateTime? quoteDate,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(panamaPol) || string.IsNullOrWhiteSpace(finalDestination))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidPanamaContinuationRoute",
                "El POL de Panamá y el destino final son obligatorios para buscar el segundo tramo marítimo.",
                httpContext);
        }

        var result = await dispatcher.DispatchAsync(
            new GetPanamaContinuationRatesQuery(
                panamaPol,
                finalDestination,
                containerType,
                quoteDate?.Date),
            cancellationToken);

        return EndpointResults.FromResult(result, httpContext);
    }
}

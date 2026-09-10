using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Imports.InactivateImportRate;
using Dhole.Pricing.Contracts.Imports.Request;
using Dhole.Pricing.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Dhole.Pricing.Api.Endpoints;

public static class ImportRateInactivationEndpoints
{
    public static IEndpointRouteBuilder MapImportRateInactivationEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        var group = app.MapGroup("/api/pricing/import-rates")
            .WithTags("Imported FCL Rates")
            .RequireAuthorization();

        group
            .MapPost("/inactivate", InactivateImportRatesAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        return app;
    }

    private static async Task<IResult> InactivateImportRatesAsync(
        [FromBody] ApproveImportRateBatchRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var ids = request.Ids
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return EndpointResults.BadRequest(
                "Pricing.ImportFclRateIdsRequired",
                "Debe seleccionar al menos una tarifa importada para inactivar.",
                httpContext
            );
        }

        var currentUserId = httpContext.GetCurrentUserId();
        foreach (var importRateId in ids)
        {
            var result = await dispatcher.DispatchAsync(
                new InactivateImportRateCommand(importRateId, currentUserId),
                cancellationToken
            );

            if (result.IsFailure)
            {
                return EndpointResults.FromResult(result, httpContext);
            }
        }

        return Results.NoContent();
    }
}

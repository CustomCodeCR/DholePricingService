using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Imports.CreateImportRate;
using Dhole.Pricing.Contracts.Imports.Request;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Dhole.Pricing.Api.Endpoints;

public static class ManualOceanFreightEndpoints
{
    private static readonly Guid ManualProfileId = Guid.Parse("00000000-0000-0000-0000-00000000f006");

    public static IEndpointRouteBuilder MapManualOceanFreightEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pricing/import-rates/manual-ocean-freight", SaveManualOceanFreightAsync)
            .WithTags("Imported FCL Rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);

        return app;
    }

    private static async Task<IResult> SaveManualOceanFreightAsync(
        [FromBody] SaveManualOceanFreightRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (request.OceanFreight <= 0m)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidManualOceanFreight",
                "El costo del flete marítimo debe ser mayor que cero.",
                httpContext
            );
        }

        if (request.ValidTo.Date < request.ValidFrom.Date)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidManualOceanFreightValidity",
                "La vigencia final de la tarifa debe ser igual o posterior a la vigencia inicial.",
                httpContext
            );
        }

        try
        {
            var totalSale = request.TotalSale > 0m ? request.TotalSale : request.OceanFreight;
            var profit = totalSale - request.OceanFreight;
            var margin = totalSale > 0m ? (profit / totalSale) * 100m : 0m;
            var comment = string.IsNullOrWhiteSpace(request.SpaceComment)
                ? null
                : request.SpaceComment.Trim();

            var rawDataJson = JsonSerializer.Serialize(
                new
                {
                    source = "manual",
                    screen = 6,
                    comments = comment,
                    oceanFreightCost = request.OceanFreight,
                    oceanFreightSale = totalSale,
                }
            );

            var result = await dispatcher.DispatchAsync(
                new CreateImportRateCommand(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    ImportSourceType.Manual,
                    CatalogSnapshot.Create(
                        ManualProfileId,
                        "Flete marítimo manual",
                        "MANUAL_OCEAN",
                        "manual-ocean-freight"
                    ),
                    ToSnapshot(request.Pol),
                    ToSnapshot(request.Poe),
                    ToSnapshot(request.Pod),
                    ToSnapshot(request.Carrier),
                    ToSnapshot(request.Agent),
                    ToSnapshot(request.ContainerType),
                    ToSnapshot(request.Currency),
                    request.Commodity,
                    comment,
                    request.OceanFreight,
                    0m,
                    0m,
                    0m,
                    request.OceanFreight,
                    totalSale,
                    profit,
                    margin,
                    Math.Max(0, request.FreeDays),
                    request.TransitDays.HasValue ? Math.Max(0, request.TransitDays.Value) : null,
                    request.ValidFrom.Date,
                    request.ValidTo.Date,
                    rawDataJson,
                    httpContext.GetCurrentUserId()
                ),
                cancellationToken
            );

            return EndpointResults.FromResult(result, httpContext);
        }
        catch (InvalidOperationException exception)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidManualOceanFreightCatalog",
                exception.Message,
                httpContext
            );
        }
    }

    private static CatalogSnapshot ToSnapshot(ImportCatalogSnapshotRequest request)
    {
        return CatalogSnapshot.Create(request.Id, request.Name, request.Code, request.Slug);
    }
}

public sealed record SaveManualOceanFreightRequest(
    ImportCatalogSnapshotRequest Pol,
    ImportCatalogSnapshotRequest Poe,
    ImportCatalogSnapshotRequest Pod,
    ImportCatalogSnapshotRequest Carrier,
    ImportCatalogSnapshotRequest Agent,
    ImportCatalogSnapshotRequest ContainerType,
    ImportCatalogSnapshotRequest Currency,
    string? Commodity,
    string? SpaceComment,
    decimal OceanFreight,
    decimal TotalSale,
    int FreeDays,
    int? TransitDays,
    DateTime ValidFrom,
    DateTime ValidTo
);

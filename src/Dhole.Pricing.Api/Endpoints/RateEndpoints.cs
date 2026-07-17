using System.Security.Claims;
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Rates.ApproveRateMargin;
using Dhole.Pricing.Application.Features.Rates.CreateRate;
using Dhole.Pricing.Application.Features.Rates.DeleteRate;
using Dhole.Pricing.Application.Features.Rates.DuplicateRate;
using Dhole.Pricing.Application.Features.Rates.GetRateById;
using Dhole.Pricing.Application.Features.Rates.GetRates;
using Dhole.Pricing.Application.Features.Rates.RejectRateMargin;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Application.Features.Rates.UpdateRate;
using Dhole.Pricing.Contracts.Rates.Request;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateEndpoints
{
    public static IEndpointRouteBuilder MapRateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rates").WithTags("Rates").RequireAuthorization();

        group.MapGet("/", GetRatesAsync).RequireScope(PricingConstants.Scopes.RateView);

        group
            .MapGet("/{rateId:guid}", GetRateByIdAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        group.MapPost("/", CreateRateAsync).RequireScope(PricingConstants.Scopes.RateCreate);

        group
            .MapPut("/{rateId:guid}", UpdateRateAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        group
            .MapPost("/{rateId:guid}/duplicate", DuplicateRateAsync)
            .RequireScope(PricingConstants.Scopes.RateCreate);

        group
            .MapPost("/{rateId:guid}/margin/approve", ApproveRateMarginAsync)
            .RequireScope(PricingConstants.Scopes.RateApproveLowMargin);

        group
            .MapPost("/{rateId:guid}/margin/reject", RejectRateMarginAsync)
            .RequireScope(PricingConstants.Scopes.RateApproveLowMargin);

        group
            .MapPatch("/{rateId:guid}/status", SetRateStatusAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        group.MapDelete("/", DeleteRatesAsync).RequireScope(PricingConstants.Scopes.RateDelete);

        return app;
    }

    private static async Task<IResult> GetRatesAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        string? idtraNumber,
        string? quoNumber,
        Guid? sourceImportFclRateId,
        Guid? agentId,
        Guid? carrierId,
        Guid? polId,
        Guid? poeId,
        Guid? podId,
        Guid? containerTypeId,
        Guid? currencyId,
        RateStatus? status,
        bool? requiredApproval,
        DateTime? quoteDate,
        DateTime? validFrom,
        DateTime? validTo,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetRatesQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                idtraNumber,
                quoNumber,
                sourceImportFclRateId,
                agentId,
                carrierId,
                polId,
                poeId,
                podId,
                containerTypeId,
                currencyId,
                status,
                requiredApproval,
                quoteDate,
                validFrom,
                validTo
            ),
            cancellationToken
        );

        return EndpointResults.FromPaged(result, httpContext);
    }

    private static async Task<IResult> GetRateByIdAsync(
        Guid rateId,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetRateByIdQuery(rateId),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CreateRateAsync(
        CreateRateRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (
            request.SourceImportFclRateId.HasValue
            && !HasScope(httpContext.User, PricingConstants.Scopes.ImportFclRateCreateAsRate)
        )
        {
            return Results.Forbid();
        }

        var details = new List<CreateRateDetailCommandItem>();

        foreach (var detail in request.Details)
        {
            if (!TryParseDefinedEnum(detail.CostDetailType, out CostDetailType costDetailType))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostDetailType",
                    $"El rubro '{detail.CostDetailType}' no es válido.",
                    httpContext
                );
            }

            if (!TryParseDefinedEnum(detail.CostType, out CostType costType))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostType",
                    $"El tipo '{detail.CostType}' no es válido.",
                    httpContext
                );
            }

            details.Add(
                new CreateRateDetailCommandItem(
                    detail.CostId,
                    detail.Name,
                    costDetailType,
                    costType,
                    detail.CurrencyId,
                    detail.CurrencyName,
                    detail.CurrencyCode,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Notes
                )
            );
        }

        /*
         * Este valor se calcula desde los claims.
         * Nunca debe recibirse desde el frontend.
         */
        var canApproveImportedRate = HasScope(
            httpContext.User,
            PricingConstants.Scopes.ImportFclRateApprove
        );

        var result = await dispatcher.DispatchAsync(
            new CreateRateCommand(
                request.SourceImportFclRateId,
                request.AgentId,
                request.AgentName,
                request.AgentCode,
                request.CarrierId,
                request.CarrierName,
                request.CarrierCode,
                request.PolId,
                request.PolName,
                request.PolCode,
                request.PoeId,
                request.PoeName,
                request.PoeCode,
                request.PodId,
                request.PodName,
                request.PodCode,
                request.ContainerTypeId,
                request.ContainerTypeName,
                request.ContainerTypeCode,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                request.FreeDays,
                request.ValidFrom,
                request.ValidTo,
                request.ContainerQuantity,
                request.ClientName,
                request.IdtraNumber,
                request.QuoNumber,
                request.Includes,
                request.SubjectTo,
                request.Excludes,
                request.TransitDays,
                details,
                canApproveImportedRate,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> UpdateRateAsync(
        Guid rateId,
        UpdateRateRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var extraDetails = new List<UpsertRateExtraDetailCommandItem>();

        foreach (var detail in request.ExtraDetails)
        {
            if (!TryParseDefinedEnum(detail.CostDetailType, out CostDetailType costDetailType))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostDetailType",
                    $"El rubro '{detail.CostDetailType}' no es válido.",
                    httpContext
                );
            }

            if (!TryParseDefinedEnum(detail.CostType, out CostType costType))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostType",
                    $"El tipo '{detail.CostType}' no es válido.",
                    httpContext
                );
            }

            extraDetails.Add(
                new UpsertRateExtraDetailCommandItem(
                    detail.Id,
                    detail.CostId,
                    detail.Name,
                    costDetailType,
                    costType,
                    detail.CurrencyId,
                    detail.CurrencyName,
                    detail.CurrencyCode,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Notes
                )
            );
        }

        var result = await dispatcher.DispatchAsync(
            new UpdateRateCommand(
                rateId,
                request.AgentId,
                request.AgentName,
                request.AgentCode,
                request.CarrierId,
                request.CarrierName,
                request.CarrierCode,
                request.PolId,
                request.PolName,
                request.PolCode,
                request.PoeId,
                request.PoeName,
                request.PoeCode,
                request.PodId,
                request.PodName,
                request.PodCode,
                request.ContainerTypeId,
                request.ContainerTypeName,
                request.ContainerTypeCode,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                request.FreeDays,
                request.ValidFrom,
                request.ValidTo,
                request.ContainerQuantity,
                request.ClientName,
                request.IdtraNumber,
                request.QuoNumber,
                request.Includes,
                request.SubjectTo,
                request.Excludes,
                request.TransitDays,
                extraDetails,
                request.RemovedExtraDetailIds,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DuplicateRateAsync(
        Guid rateId,
        DuplicateRateRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DuplicateRateCommand(
                rateId,
                request.ValidFrom,
                request.ValidTo,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ApproveRateMarginAsync(
        Guid rateId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new ApproveRateMarginCommand(rateId, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> RejectRateMarginAsync(
        Guid rateId,
        RejectRateMarginRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new RejectRateMarginCommand(rateId, request.Reason, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> SetRateStatusAsync(
        Guid rateId,
        SetRateStatusRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.Status, out RateStatus status)
            || status is not (RateStatus.Sent or RateStatus.AcceptedByClient or RateStatus.RejectedByClient))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidRateStatus",
                "El estado comercial de la tarifa no es válido.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new SetRateStatusCommand(rateId, status, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DeleteRatesAsync(
        [FromBody] DeleteRateBatchRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DeleteRateCommand(request.Ids, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        return user
            .Claims.Where(claim => claim.Type is "scope" or "scp")
            .SelectMany(claim =>
                claim.Value.Split(
                    [' ', ','],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            .Any(scope => string.Equals(scope, requiredScope, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}

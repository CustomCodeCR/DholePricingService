using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Costs.Create;
using Dhole.Pricing.Application.Features.Costs.Delete;
using Dhole.Pricing.Application.Features.Costs.GetCostById;
using Dhole.Pricing.Application.Features.Costs.GetCosts;
using Dhole.Pricing.Application.Features.Costs.GetCostsForSelect;
using Dhole.Pricing.Application.Features.Costs.SetActive;
using Dhole.Pricing.Application.Features.Costs.Update;
using Dhole.Pricing.Contracts.Costs.Request;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Api.Endpoints;

public static class CostEndpoints
{
    public static IEndpointRouteBuilder MapCostEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/costs").WithTags("Costs").RequireAuthorization();

        group.MapGet("/", GetCostsAsync).RequireScope(PricingConstants.Scopes.CostView);

        group
            .MapGet("/select", GetCostsForSelectAsync)
            .RequireScope(PricingConstants.Scopes.CostSelect);

        group
            .MapGet("/{costId:guid}", GetCostByIdAsync)
            .RequireScope(PricingConstants.Scopes.CostView);

        group.MapPost("/", CreateCostAsync).RequireScope(PricingConstants.Scopes.CostCreate);

        group
            .MapPut("/{costId:guid}", UpdateCostAsync)
            .RequireScope(PricingConstants.Scopes.CostUpdate);

        group
            .MapPatch("/{costId:guid}/active", SetCostActiveAsync)
            .RequireScope(PricingConstants.Scopes.CostSetActive);

        group
            .MapDelete("/{costId:guid}", DeleteCostAsync)
            .RequireScope(PricingConstants.Scopes.CostDelete);

        return app;
    }

    private static async Task<IResult> GetCostsAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        CostType? costType,
        CostDetailType? costDetailType,
        Guid? carrierId,
        Guid? agentId,
        Guid? portId,
        CostPortRole? portRole,
        Guid? currencyId,
        bool? isActive,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetCostsQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                costType,
                costDetailType,
                carrierId,
                agentId,
                portId,
                portRole,
                currencyId,
                isActive
            ),
            cancellationToken
        );

        return EndpointResults.FromPaged(result, httpContext);
    }

    private static async Task<IResult> GetCostsForSelectAsync(
        string? search,
        CostType? costType,
        CostDetailType? costDetailType,
        Guid? carrierId,
        Guid? agentId,
        Guid? portId,
        CostPortRole? portRole,
        Guid? currencyId,
        bool? isActive,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetCostsForSelectQuery(
                search,
                costType,
                costDetailType,
                carrierId,
                agentId,
                portId,
                portRole,
                currencyId,
                isActive ?? true
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> GetCostByIdAsync(
        Guid costId,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetCostByIdQuery(costId),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CreateCostAsync(
        CreateCostRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.CostType, out CostType costType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidCostType",
                "El tipo de costo no es válido.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.CostDetailType, out CostDetailType costDetailType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidCostDetailType",
                "El rubro del costo no es válido.",
                httpContext
            );
        }

        CostPortRole? portRole = null;

        if (!string.IsNullOrWhiteSpace(request.PortRole))
        {
            if (!TryParseDefinedEnum(request.PortRole, out CostPortRole parsedPortRole))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostPortRole",
                    "El rol del puerto no es válido.",
                    httpContext
                );
            }

            portRole = parsedPortRole;
        }

        if (
            !request.IsAccountant
            && (
                !request.PortId.HasValue
                || request.PortId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.PortName)
                || string.IsNullOrWhiteSpace(request.PortCode)
                || !portRole.HasValue
            )
        )
        {
            return EndpointResults.BadRequest(
                "Pricing.CostPortRequired",
                "El puerto y su rol son obligatorios para costos no contables.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new CreateCostCommand(
                request.Name,
                costType,
                costDetailType,
                request.CarrierId,
                request.CarrierName,
                request.CarrierCode,
                request.AgentId,
                request.AgentName,
                request.AgentCode,
                request.PortId,
                request.PortName,
                request.PortCode,
                portRole,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                request.CostAmount,
                request.SaleAmount,
                request.Notes,
                request.IsAccountant,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> UpdateCostAsync(
        Guid costId,
        UpdateCostRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.CostType, out CostType costType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidCostType",
                "El tipo de costo no es válido.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.CostDetailType, out CostDetailType costDetailType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidCostDetailType",
                "El rubro del costo no es válido.",
                httpContext
            );
        }

        CostPortRole? portRole = null;

        if (!string.IsNullOrWhiteSpace(request.PortRole))
        {
            if (!TryParseDefinedEnum(request.PortRole, out CostPortRole parsedPortRole))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostPortRole",
                    "El rol del puerto no es válido.",
                    httpContext
                );
            }

            portRole = parsedPortRole;
        }

        if (
            !request.IsAccountant
            && (
                !request.PortId.HasValue
                || request.PortId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.PortName)
                || string.IsNullOrWhiteSpace(request.PortCode)
                || !portRole.HasValue
            )
        )
        {
            return EndpointResults.BadRequest(
                "Pricing.CostPortRequired",
                "El puerto y su rol son obligatorios para costos no contables.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new UpdateCostCommand(
                costId,
                request.Name,
                costType,
                costDetailType,
                request.CarrierId,
                request.CarrierName,
                request.CarrierCode,
                request.AgentId,
                request.AgentName,
                request.AgentCode,
                request.PortId,
                request.PortName,
                request.PortCode,
                portRole,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                request.CostAmount,
                request.SaleAmount,
                request.Notes,
                request.IsAccountant,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> SetCostActiveAsync(
        Guid costId,
        SetCostActiveRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new SetCostActiveCommand(costId, request.IsActive, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DeleteCostAsync(
        Guid costId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DeleteCostCommand(costId, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}

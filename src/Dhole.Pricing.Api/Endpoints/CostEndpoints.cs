using System.Text.Json;
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Application.Features.Costs.Create;
using Dhole.Pricing.Application.Features.Costs.Delete;
using Dhole.Pricing.Application.Features.Costs.GetCostById;
using Dhole.Pricing.Application.Features.Costs.GetCosts;
using Dhole.Pricing.Application.Features.Costs.GetCostsForSelect;
using Dhole.Pricing.Application.Features.Costs.SetActive;
using Dhole.Pricing.Application.Features.Costs.Update;
using Dhole.Pricing.Contracts.Costs.Request;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
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
            .MapGet("/export.xlsx", ExportActiveCostsAsync)
            .RequireScope(PricingConstants.Scopes.CostView);

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
        string[]? costType,
        string[]? costDetailType,
        string[]? carrierId,
        string[]? agentId,
        string[]? portId,
        string[]? portRole,
        string[]? currencyId,
        string[]? isActive,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetCostsQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                ParseEnumList<CostType>(costType),
                ParseEnumList<CostDetailType>(costDetailType),
                ParseGuidList(carrierId),
                ParseGuidList(agentId),
                ParseGuidList(portId),
                ParseEnumList<CostPortRole>(portRole),
                ParseGuidList(currencyId),
                ParseBoolList(isActive)
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
        Guid? polId,
        Guid? poeId,
        Guid? podId,
        Guid? incotermId,
        ShipmentMode? shipmentMode,
        bool? applicableToContext,
        string? serviceIds,
        Guid? importRateId,
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
                isActive ?? true,
                polId,
                poeId,
                podId,
                incotermId,
                shipmentMode,
                applicableToContext ?? false,
                ParseGuidList(serviceIds),
                importRateId
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

        ShipmentMode? shipmentMode = null;
        if (!string.IsNullOrWhiteSpace(request.ShipmentMode))
        {
            if (!TryParseDefinedEnum(request.ShipmentMode, out ShipmentMode parsedShipmentMode))
                return EndpointResults.BadRequest("Pricing.InvalidShipmentMode", "La modalidad no es válida.", httpContext);
            shipmentMode = parsedShipmentMode;
        }

        if (!TryParseDefinedEnum(request.ChargeBasis, out ChargeBasis chargeBasis))
            return EndpointResults.BadRequest("Pricing.InvalidChargeBasis", "La base de cobro no es válida.", httpContext);

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
                request.PolId,
                request.PolName,
                request.PolCode,
                request.PoeId,
                request.PoeName,
                request.PoeCode,
                request.PodId,
                request.PodName,
                request.PodCode,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                request.CostAmount,
                request.SaleAmount,
                request.Notes,
                request.IsAccountant,
                (request.Incoterms ?? [])
                    .Select(x => new CostIncotermSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                (request.Services ?? [])
                    .Select(x => new CostServiceSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                shipmentMode,
                chargeBasis,
                request.MinimumCostAmount,
                request.MinimumSaleAmount,
                request.KgPerCbm,
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

        ShipmentMode? shipmentMode = null;
        if (!string.IsNullOrWhiteSpace(request.ShipmentMode))
        {
            if (!TryParseDefinedEnum(request.ShipmentMode, out ShipmentMode parsedShipmentMode))
                return EndpointResults.BadRequest("Pricing.InvalidShipmentMode", "La modalidad no es válida.", httpContext);
            shipmentMode = parsedShipmentMode;
        }

        if (!TryParseDefinedEnum(request.ChargeBasis, out ChargeBasis chargeBasis))
            return EndpointResults.BadRequest("Pricing.InvalidChargeBasis", "La base de cobro no es válida.", httpContext);

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
                request.PolId,
                request.PolName,
                request.PolCode,
                request.PoeId,
                request.PoeName,
                request.PoeCode,
                request.PodId,
                request.PodName,
                request.PodCode,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                request.CostAmount,
                request.SaleAmount,
                request.Notes,
                request.IsAccountant,
                (request.Incoterms ?? [])
                    .Select(x => new CostIncotermSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                (request.Services ?? [])
                    .Select(x => new CostServiceSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                shipmentMode,
                chargeBasis,
                request.MinimumCostAmount,
                request.MinimumSaleAmount,
                request.KgPerCbm,
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


    private static async Task<IResult> ExportActiveCostsAsync(
        ICostRepository costs,
        IPricingReportsClient reports,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var activeCosts = await costs.GetActiveCostsAsync(
                cancellationToken: cancellationToken
            );

            var exportRows = activeCosts
                .Select(cost => new Dictionary<string, object?>
                {
                    ["ID"] = cost.Id,
                    ["Nombre del costo"] = cost.Name,
                    ["Aplicación"] = CostTypeLabel(cost.CostType),
                    ["Rubro"] = CostDetailTypeLabel(cost.CostDetailType),
                    ["Naviera"] = cost.CarrierName ?? string.Empty,
                    ["Agente"] = cost.AgentName ?? string.Empty,
                    ["POL"] = cost.PolName ?? string.Empty,
                    ["POE"] = cost.PoeName ?? string.Empty,
                    ["POD"] = cost.PodName ?? string.Empty,
                    ["Puerto"] = cost.PortName ?? string.Empty,
                    ["Rol puerto"] = cost.PortRole?.ToString() ?? string.Empty,
                    ["Incoterms"] = string.Join(
                        ", ",
                        cost.Incoterms.Select(x => x.IncotermCode)
                    ),
                    ["Servicios"] = string.Join(
                        ", ",
                        cost.Services.Select(x => x.ServiceName)
                    ),
                    ["Modalidad"] = cost.ShipmentMode?.ToString() ?? "Todas",
                    ["Moneda"] = cost.CurrencyCode,
                    ["Monto costo"] = cost.CostAmount,
                    ["Venta"] = cost.SaleAmount,
                    ["Utilidad"] = cost.UtilityAmount,
                    ["Base de cobro"] = ChargeBasisLabel(cost.ChargeBasis),
                    ["Mínimo costo"] = cost.MinimumCostAmount,
                    ["Mínimo venta"] = cost.MinimumSaleAmount,
                    ["KG por CBM"] = cost.KgPerCbm,
                    ["Contable"] = cost.IsAccountant ? "Sí" : "No",
                    ["Estado"] = "Activo",
                    ["Notas"] = cost.Notes ?? string.Empty,
                })
                .ToArray();

            var generated = await reports.GenerateTabularAsync(
                "xlsx",
                JsonSerializer.Serialize(exportRows),
                $"costos-pricing-activos-{DateTime.UtcNow:yyyyMMdd}",
                "Costos activos",
                cancellationToken
            );

            return Results.File(
                generated.Content,
                generated.ContentType,
                generated.FileName
            );
        }
        catch (HttpRequestException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem(
                title: "Pricing cost export error",
                detail: $"DholeReports no pudo generar el Excel: {exception.Message}",
                statusCode: StatusCodes.Status502BadGateway,
                instance: httpContext.Request.Path.Value
            );
        }
    }

    private static string CostTypeLabel(CostType value) => value switch
    {
        CostType.Fixed => "Fijo",
        CostType.Optional => "Opcional",
        CostType.Variable => "Variable",
        _ => value.ToString(),
    };

    private static string CostDetailTypeLabel(CostDetailType value) => value switch
    {
        CostDetailType.Freight => "Flete internacional",
        CostDetailType.AgentCharge => "Costo de agente",
        CostDetailType.OriginCharge => "Origen",
        CostDetailType.DestinationCharge => "Destino",
        CostDetailType.PortCharge => "Puerto",
        CostDetailType.CustomsCharge => "Aduana",
        CostDetailType.InlandTransport => "Transporte interno",
        CostDetailType.Documentation => "Documentación",
        CostDetailType.Insurance => "Seguro",
        CostDetailType.Other => "Otro",
        _ => value.ToString(),
    };

    private static string ChargeBasisLabel(ChargeBasis value) => value switch
    {
        ChargeBasis.PerShipment => "Por embarque",
        ChargeBasis.PerService => "Por servicio",
        ChargeBasis.PerContainer => "Por contenedor",
        ChargeBasis.PerTeu => "Por TEU",
        ChargeBasis.PerTruck => "Por camión",
        ChargeBasis.PerCbm => "Por CBM",
        ChargeBasis.PerChargeableCbm => "Por CBM cobrable",
        ChargeBasis.PerKg => "Por KG",
        ChargeBasis.Per100Kg => "Por 100 KG",
        ChargeBasis.PerTon => "Por tonelada",
        ChargeBasis.PerPallet => "Por pallet",
        ChargeBasis.PerPackage => "Por bulto",
        ChargeBasis.PerDocument => "Por BL / documento",
        _ => value.ToString(),
    };

    private static IReadOnlyCollection<Guid> ParseGuidList(string? value) =>
        ParseGuidList(value is null ? null : [value]);

    private static IReadOnlyCollection<Guid> ParseGuidList(IEnumerable<string>? values)
    {
        var ids = new HashSet<Guid>();
        foreach (var value in SplitMultiValues(values))
        {
            if (Guid.TryParse(value, out var id) && id != Guid.Empty)
            {
                ids.Add(id);
            }
        }

        return ids.ToArray();
    }

    private static IReadOnlyCollection<TEnum> ParseEnumList<TEnum>(IEnumerable<string>? values)
        where TEnum : struct, Enum
    {
        var parsed = new HashSet<TEnum>();
        foreach (var value in SplitMultiValues(values))
        {
            if (TryParseDefinedEnum(value, out TEnum item))
            {
                parsed.Add(item);
            }
        }

        return parsed.ToArray();
    }

    private static IReadOnlyCollection<bool> ParseBoolList(IEnumerable<string>? values)
    {
        var parsed = new HashSet<bool>();
        foreach (var value in SplitMultiValues(values))
        {
            if (bool.TryParse(value, out var item))
            {
                parsed.Add(item);
            }
        }

        return parsed.ToArray();
    }

    private static IEnumerable<string> SplitMultiValues(IEnumerable<string>? values)
    {
        if (values is null) yield break;

        foreach (var value in values)
        {
            foreach (var item in value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            ))
            {
                yield return item;
            }
        }
    }

    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}

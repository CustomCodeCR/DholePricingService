using System.Data;
using System.Security.Claims;
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Features.Rates.ApproveRateMargin;
using Dhole.Pricing.Application.Features.Rates.CreateRate;
using Dhole.Pricing.Application.Features.Rates.DeleteRate;
using Dhole.Pricing.Application.Features.Rates.DuplicateRate;
using Dhole.Pricing.Application.Features.Rates.GetRateById;
using Dhole.Pricing.Application.Features.Rates.GetRateRevisions;
using Dhole.Pricing.Application.Features.Rates.GetRateDashboard;
using Dhole.Pricing.Application.Features.Rates.GetRates;
using Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;
using Dhole.Pricing.Application.Features.Rates.RejectRateMargin;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Application.Features.Rates.UpdateRate;
using Dhole.Pricing.Contracts.Rates.Request;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateEndpoints
{
    public static IEndpointRouteBuilder MapRateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rates").WithTags("Rates").RequireAuthorization();

        group.MapGet("/", GetRatesAsync).RequireScope(PricingConstants.Scopes.RateView);

        group
            .MapGet("/dashboard", GetRateDashboardAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        group
            .MapGet("/{rateId:guid}", GetRateByIdAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        group
            .MapGet("/{rateId:guid}/revisions", GetRateRevisionsAsync)
            .RequireScope(PricingConstants.Scopes.RateView);


        group
            .MapGet("/report-template-definition", GetRateReportTemplateDefinition)
            .RequireScope(PricingConstants.Scopes.RateView);

        group
            .MapPost("/{rateId:guid}/documents", GenerateRateDocumentAsync)
            .RequireScope(PricingConstants.Scopes.RateReportGenerate);

        group
            .MapGet("/exchange-rate/usd-crc", GetUsdCrcExchangeRateAsync)
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


    private static async Task<IResult> GetUsdCrcExchangeRateAsync(
        IPricingExchangeRateProvider exchangeRateProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await exchangeRateProvider.GetUsdCrcAsync(cancellationToken);
        if (snapshot is null)
        {
            return Results.Problem(
                title: "Tipo de cambio no disponible",
                detail: "No fue posible consultar el tipo de cambio del dólar en Hacienda en este momento.",
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }

        return EndpointResults.Ok(new
        {
            purchase = snapshot.Purchase,
            sale = snapshot.Sale,
            rateDate = snapshot.RateDate,
            capturedAtUtc = snapshot.CapturedAtUtc,
            source = snapshot.Source
        });
    }

    private static IResult GetRateReportTemplateDefinition()
    {
        var sampleData = new
        {
            company = new
            {
                name = "Grupo Castro Fallas",
                legalName = "Grupo Castro Fallas",
                phone = "+506 0000-0000",
                email = "pricing@empresa.com",
                website = "https://logisticacastrofallas.com",
                logoDataUri = ""
            },
            generated = new { date = "06/08/2026", time = "11:00" },
            rate = new
            {
                rateCode = "RATE-ABCDE-123456",
                quoteNumber = "QUO-ABCDE-123456",
                idtraNumber = "IDTRA-2026-00125",
                clientName = "Cliente de ejemplo",
                agent = "Agente de ejemplo",
                carrier = "MSC",
                pol = "Shanghai",
                poe = "Moín",
                pod = "Puerto Caldera",
                route = "Shanghai → Puerto Caldera vía Moín",
                rateType = "TARIFARIO",
                shipmentMode = "Fcl",
                totalPackages = 0,
                totalPallets = 0,
                totalWeightKg = 0m,
                totalVolumeCbm = 0m,
                kgPerCbm = 500m,
                chargeableQuantity = 2m,
                containerType = "1 x 40 HC + 1 x 20 DV",
                containerQuantity = 2,
                containerSummary = "1 x 40 HC + 1 x 20 DV",
                currency = "USD",
                freeDays = 21,
                transitTime = "28 días",
                transitDays = 28,
                validFrom = "08/08/2026",
                validTo = "14/08/2026",
                total = "USD 12,730.00",
                totalAmount = 12730.00m,
                includes = "Flete marítimo y días libres indicados.",
                subjectTo = "Espacio y equipo disponibles.",
                excludes = "Impuestos y gastos no indicados.",
                status = "Open"
            },
            containers = new[]
            {
                new { containerTypeId = Guid.Empty, containerType = "40 HC", containerTypeName = "40 HC", containerTypeCode = "40HC", quantity = 1, label = "1 x 40 HC" },
                new { containerTypeId = Guid.Empty, containerType = "20 DV", containerTypeName = "20 DV", containerTypeCode = "20DV", quantity = 1, label = "1 x 20 DV" }
            },
            items = new[]
            {
                new { description = "Flete marítimo", quantity = 2, unitSale = "USD 6,300.00", unitSaleAmount = 6300m, lineTotal = "USD 12,600.00", lineTotalAmount = 12600m, notes = "" },
                new { description = "ISPS", quantity = 2, unitSale = "USD 15.00", unitSaleAmount = 15m, lineTotal = "USD 30.00", lineTotalAmount = 30m, notes = "Por contenedor" },
                new { description = "P/S", quantity = 2, unitSale = "USD 50.00", unitSaleAmount = 50m, lineTotal = "USD 100.00", lineTotalAmount = 100m, notes = "Por contenedor" }
            },
            rows = new[]
            {
                new Dictionary<string, object?> { ["Concepto"] = "Flete marítimo", ["Cantidad"] = 2, ["Moneda"] = "USD", ["Precio unitario"] = 6300m, ["Total"] = 12600m, ["Notas"] = "" }
            }
        };

        var variables = new[]
        {
            "company.name", "company.legalName", "company.phone", "company.email", "company.website", "company.logoDataUri",
            "generated.date", "generated.time",
            "rate.rateCode", "rate.quoteNumber", "rate.idtraNumber", "rate.clientName", "rate.agent", "rate.carrier",
            "rate.pol", "rate.poe", "rate.pod", "rate.route", "rate.rateType", "rate.shipmentMode", "rate.totalPackages", "rate.totalPallets",
            "rate.totalWeightKg", "rate.totalVolumeCbm", "rate.kgPerCbm", "rate.chargeableQuantity",
            "rate.containerType", "rate.containerQuantity", "rate.containerSummary", "rate.currency",
            "containers[].containerType", "containers[].containerTypeName", "containers[].containerTypeCode", "containers[].quantity", "containers[].label",
            "rate.freeDays", "rate.transitTime", "rate.transitDays", "rate.validFrom", "rate.validTo", "rate.total", "rate.totalAmount",
            "rate.includes", "rate.subjectTo", "rate.excludes", "rate.status",
            "items[].description", "items[].quantity", "items[].unitSale", "items[].unitSaleAmount", "items[].lineTotal", "items[].lineTotalAmount", "items[].notes"
        };

        return EndpointResults.Ok(new
        {
            templateCode = "pricing-fcl-client-quote",
            name = "Cotización FCL para cliente",
            pageSize = "A4",
            orientation = "Portrait",
            variables,
            sampleData
        });
    }

    private static async Task<IResult> GenerateRateDocumentAsync(
        Guid rateId,
        GenerateRateDocumentRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchAsync(
            new GenerateRateDocumentCommand(rateId, request.TemplateCode, request.Format),
            cancellationToken);

        return result.IsSuccess
            ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> GetRateDashboardAsync(
        DateTime? createdFrom,
        DateTime? createdTo,
        DateTime? modifiedFrom,
        DateTime? modifiedTo,
        DateTime? validityFrom,
        DateTime? validityTo,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (createdFrom.HasValue && createdTo.HasValue && createdFrom > createdTo)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidCreatedDateRange",
                "La fecha inicial de creación no puede ser posterior a la fecha final.",
                httpContext
            );
        }

        if (modifiedFrom.HasValue && modifiedTo.HasValue && modifiedFrom > modifiedTo)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidModifiedDateRange",
                "La fecha inicial de modificación no puede ser posterior a la fecha final.",
                httpContext
            );
        }

        if (validityFrom.HasValue && validityTo.HasValue && validityFrom > validityTo)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidValidityDateRange",
                "La fecha inicial de vigencia no puede ser posterior a la fecha final.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new GetRateDashboardQuery(
                createdFrom,
                createdTo,
                modifiedFrom,
                modifiedTo,
                validityFrom,
                validityTo
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
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

    private static async Task<IResult> GetRateRevisionsAsync(
        Guid rateId, IQueryDispatcher dispatcher, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchAsync(new GetRateRevisionsQuery(rateId), cancellationToken);
        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CreateRateAsync(
        CreateRateRequest request,
        ICommandDispatcher dispatcher,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var termValidation = ValidateExclusiveRateTerms(request.Includes, request.SubjectTo, request.Excludes, httpContext);
        if (termValidation is not null) return termValidation;

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

            ChargeBasis? chargeBasis = null;
            if (!string.IsNullOrWhiteSpace(detail.ChargeBasis))
            {
                if (!TryParseDefinedEnum(detail.ChargeBasis, out ChargeBasis parsedChargeBasis))
                {
                    return EndpointResults.BadRequest(
                        "Pricing.InvalidChargeBasis",
                        $"La base de cobro '{detail.ChargeBasis}' no es válida.",
                        httpContext
                    );
                }
                chargeBasis = parsedChargeBasis;
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
                    detail.Notes,
                    detail.Quantity,
                    chargeBasis,
                    detail.ApplyDestinationTax,
                    detail.DestinationTaxRate
                )
            );
        }

        if (!TryParseDefinedEnum(request.ShipmentMode, out ShipmentMode shipmentMode))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidShipmentMode",
                $"La modalidad '{request.ShipmentMode}' no es válida.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.OperationType, out RateOperationType operationType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidOperationType",
                $"El tipo de operación '{request.OperationType}' no es válido.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.RateType, out RateType rateType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidRateType",
                $"El tipo de tarifa '{request.RateType}' no es válido.",
                httpContext
            );
        }

        var cargoLines = (request.CargoLines ?? Array.Empty<RateCargoLineRequest>())
            .Select(x => new Dhole.Pricing.Application.Features.Rates.RateCargoLineCommandItem(
                x.Description, x.Packages, x.Pallets, x.WeightKg, x.LengthCm, x.WidthCm, x.HeightCm))
            .ToArray();

        var containers = (request.Containers ?? Array.Empty<RateContainerRequest>())
            .Select(x => new RateContainerCommandItem(
                x.ContainerTypeId,
                x.ContainerTypeName,
                x.ContainerTypeCode,
                x.Quantity
            ))
            .ToList();

        if (containers.Count == 0)
        {
            var synthetic = SyntheticEquipment(shipmentMode, request.ContainerTypeId, request.ContainerTypeName, request.ContainerTypeCode);
            containers.Add(new RateContainerCommandItem(
                synthetic.Id, synthetic.Name, synthetic.Code, Math.Max(request.ContainerQuantity, 1)));
        }

        /*
         * Este valor se calcula desde los claims.
         * Nunca debe recibirse desde el frontend.
         */
        var canApproveImportedRate = HasScope(
            httpContext.User,
            PricingConstants.Scopes.ImportFclRateApprove
        );
        var canApproveLowMargin = HasScope(
            httpContext.User,
            PricingConstants.Scopes.RateApproveLowMargin
        );
        var freeDays = await ResolveConfiguredFreeDaysAsync(
            db,
            request.CarrierId,
            request.FreeDays,
            cancellationToken
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
                request.IncotermId,
                request.IncotermName,
                request.IncotermCode,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                freeDays,
                request.ValidFrom,
                request.ValidTo,
                request.ContainerQuantity,
                request.ClientName,
                request.ExecutiveName,
                request.IdtraNumber,
                request.QuoNumber,
                request.Includes,
                request.SubjectTo,
                request.Excludes,
                request.TransitTime,
                details,
                containers,
                rateType,
                shipmentMode,
                request.KgPerCbm,
                request.TotalPackages,
                request.TotalPallets,
                request.TotalWeightKg,
                request.TotalVolumeCbm,
                cargoLines,
                request.PickupAddress,
                request.PickupLatitude,
                request.PickupLongitude,
                request.ExchangeRatePurchase,
                request.ExchangeRateSale,
                request.ExchangeRateApplied,
                canApproveImportedRate,
                operationType,
                (request.Services ?? [])
                    .Select(x => new RateServiceSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                canApproveLowMargin,
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
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var termValidation = ValidateExclusiveRateTerms(request.Includes, request.SubjectTo, request.Excludes, httpContext);
        if (termValidation is not null) return termValidation;

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

            ChargeBasis? chargeBasis = null;
            if (!string.IsNullOrWhiteSpace(detail.ChargeBasis))
            {
                if (!TryParseDefinedEnum(detail.ChargeBasis, out ChargeBasis parsedChargeBasis))
                {
                    return EndpointResults.BadRequest(
                        "Pricing.InvalidChargeBasis",
                        $"La base de cobro '{detail.ChargeBasis}' no es válida.",
                        httpContext
                    );
                }
                chargeBasis = parsedChargeBasis;
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
                    detail.Notes,
                    detail.Quantity,
                    chargeBasis,
                    detail.ApplyDestinationTax,
                    detail.DestinationTaxRate
                )
            );
        }

        if (!TryParseDefinedEnum(request.ShipmentMode, out ShipmentMode shipmentMode))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidShipmentMode",
                $"La modalidad '{request.ShipmentMode}' no es válida.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.OperationType, out RateOperationType operationType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidOperationType",
                $"El tipo de operación '{request.OperationType}' no es válido.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.RateType, out RateType rateType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidRateType",
                $"El tipo de tarifa '{request.RateType}' no es válido.",
                httpContext
            );
        }

        var cargoLines = (request.CargoLines ?? Array.Empty<RateCargoLineRequest>())
            .Select(x => new Dhole.Pricing.Application.Features.Rates.RateCargoLineCommandItem(
                x.Description, x.Packages, x.Pallets, x.WeightKg, x.LengthCm, x.WidthCm, x.HeightCm))
            .ToArray();

        var containers = (request.Containers ?? Array.Empty<RateContainerRequest>())
            .Select(x => new UpdateRateContainerCommandItem(
                x.ContainerTypeId,
                x.ContainerTypeName,
                x.ContainerTypeCode,
                x.Quantity
            ))
            .ToList();

        if (containers.Count == 0)
        {
            var synthetic = SyntheticEquipment(shipmentMode, request.ContainerTypeId, request.ContainerTypeName, request.ContainerTypeCode);
            containers.Add(new UpdateRateContainerCommandItem(
                synthetic.Id, synthetic.Name, synthetic.Code, Math.Max(request.ContainerQuantity, 1)));
        }

        var canApproveLowMargin = HasScope(
            httpContext.User,
            PricingConstants.Scopes.RateApproveLowMargin
        );
        var freeDays = await ResolveConfiguredFreeDaysAsync(
            db,
            request.CarrierId,
            request.FreeDays,
            cancellationToken
        );

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
                request.IncotermId,
                request.IncotermName,
                request.IncotermCode,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                freeDays,
                request.ValidFrom,
                request.ValidTo,
                request.ContainerQuantity,
                request.ClientName,
                request.ExecutiveName,
                request.IdtraNumber,
                request.QuoNumber,
                request.Includes,
                request.SubjectTo,
                request.Excludes,
                request.TransitTime,
                extraDetails,
                request.RemovedExtraDetailIds,
                containers,
                rateType,
                shipmentMode,
                request.KgPerCbm,
                request.TotalPackages,
                request.TotalPallets,
                request.TotalWeightKg,
                request.TotalVolumeCbm,
                cargoLines,
                request.PickupAddress,
                request.PickupLatitude,
                request.PickupLongitude,
                canApproveLowMargin,
                operationType,
                (request.Services ?? [])
                    .Select(x => new RateServiceSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                request.ExchangeRatePurchase,
                request.ExchangeRateSale,
                request.ExchangeRateApplied,
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
            || status is not (
                RateStatus.Open
                or RateStatus.Sent
                or RateStatus.RequestedByClient
                or RateStatus.AcceptedByClient
                or RateStatus.RejectedByClient
                or RateStatus.Closed
            ))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidRateStatus",
                "El estado comercial de la tarifa no es válido.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new SetRateStatusCommand(
                rateId,
                status,
                request.Reason,
                request.IdtraNumber,
                httpContext.GetCurrentUserId()
            ),
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

    private static (Guid Id, string Name, string Code) SyntheticEquipment(
        ShipmentMode mode, Guid requestedId, string requestedName, string requestedCode)
    {
        if (requestedId != Guid.Empty && !string.IsNullOrWhiteSpace(requestedName) && !string.IsNullOrWhiteSpace(requestedCode))
            return (requestedId, requestedName, requestedCode);

        return mode switch
        {
            ShipmentMode.Lcl => (Guid.Parse("00000000-0000-4000-8000-0000000000C1"), "Carga consolidada LCL", "LCL"),
            ShipmentMode.Ftl => (Guid.Parse("00000000-0000-4000-8000-0000000000F1"), "Camión completo FTL", "FTL"),
            ShipmentMode.Ltl => (Guid.Parse("00000000-0000-4000-8000-0000000000D1"), "Carga consolidada LTL", "LTL"),
            _ => (requestedId, requestedName, requestedCode),
        };
    }

    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
    private static IResult? ValidateExclusiveRateTerms(
        string? includes,
        string? subjectTo,
        string? excludes,
        HttpContext httpContext
    )
    {
        static HashSet<string> Lines(string? value) =>
            (value ?? string.Empty)
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x =>
                {
                    var normalized = System.Text.RegularExpressions.Regex.Replace(
                        x.ToUpperInvariant(), @"[^\p{L}\p{N}]+", " "
                    ).Trim();
                    var qualifier = System.Text.RegularExpressions.Regex.Match(
                        normalized, @"\s(?:USD|EUR|CRC|IVI|IVA|ITBMS|\d)"
                    );
                    return qualifier.Success && qualifier.Index > 0
                        ? normalized[..qualifier.Index].Trim()
                        : normalized;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Repetir accidentalmente una línea dentro de la misma categoría no viola
        // exclusividad. El error aplica únicamente si el mismo ítem aparece en
        // categorías distintas: Incluye / Sujeto a / No incluye.
        var categories = new[] { Lines(includes), Lines(subjectTo), Lines(excludes) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            foreach (var item in category)
            {
                if (seen.Contains(item))
                {
                    return EndpointResults.BadRequest(
                        "Pricing.RateTermItemDuplicated",
                        "Un ítem de tarifa solo puede pertenecer a una categoría de la cotización.",
                        httpContext
                    );
                }
            }

            seen.UnionWith(category);
        }
        return null;
    }

    private static async Task<int> ResolveConfiguredFreeDaysAsync(
        ServiceDbContext db,
        Guid? carrierId,
        int fallback,
        CancellationToken cancellationToken
    )
    {
        if (!carrierId.HasValue || carrierId.Value == Guid.Empty) return Math.Max(0, fallback);

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT free_days
                FROM pricing."CarrierFreeDayRules"
                WHERE carrier_id = @carrier_id AND is_active = TRUE
                LIMIT 1;
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@carrier_id";
            parameter.Value = carrierId.Value;
            command.Parameters.Add(parameter);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? Math.Max(0, fallback) : Math.Max(0, Convert.ToInt32(value));
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

}

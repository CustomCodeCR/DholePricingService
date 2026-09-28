using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.MarketPricing.ApplyAutoPricing;
using Dhole.Pricing.Application.Features.MarketPricing.ApproveAutoPricing;
using Dhole.Pricing.Application.Features.MarketPricing.CalculateAutoPricing;
using Dhole.Pricing.Application.Features.MarketPricing.CalculateMarketBenchmark;
using Dhole.Pricing.Application.Features.MarketPricing.GetAutoPricingDecision;
using Dhole.Pricing.Application.Features.MarketPricing.GetRateMarketBenchmark;
using Dhole.Pricing.Application.Features.MarketPricing.OverrideAutoPricing;
using Dhole.Pricing.Application.Features.MarketPricing.RecalculateAutoPricing;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Contracts.MarketPricing.Request;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Api.Endpoints;

public static class MarketPricingEndpoints
{
    private const decimal DefaultTargetPercentile = 60m;
    private const decimal DefaultCompetitiveCeilingPercentile = 65m;

    public static IEndpointRouteBuilder MapMarketPricingEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        var benchmark = app
            .MapGroup("/api/pricing/market-benchmark")
            .WithTags("Market Benchmark")
            .RequireAuthorization();

        benchmark
            .MapPost("/calculate", CalculateMarketBenchmarkAsync)
            .RequireScope(PricingConstants.Scopes.MarketBenchmarkCalculate);

        var rates = app
            .MapGroup("/api/pricing/rates/{rateId:guid}")
            .WithTags("Auto Pricing")
            .RequireAuthorization();

        rates
            .MapGet("/market-benchmark", GetRateMarketBenchmarkAsync)
            .RequireScope(PricingConstants.Scopes.MarketBenchmarkView);

        rates
            .MapPost("/auto-pricing/calculate", CalculateAutoPricingAsync)
            .RequireScope(PricingConstants.Scopes.AutoPricingCalculate);

        rates
            .MapPost("/auto-pricing/recalculate", RecalculateAutoPricingAsync)
            .RequireScope(PricingConstants.Scopes.AutoPricingCalculate);

        rates
            .MapGet("/auto-pricing", GetAutoPricingAsync)
            .RequireScope(PricingConstants.Scopes.AutoPricingView);

        rates
            .MapPost("/auto-pricing/apply", ApplyAutoPricingAsync)
            .RequireScope(PricingConstants.Scopes.AutoPricingApply);

        rates
            .MapPost("/auto-pricing/override", OverrideAutoPricingAsync)
            .RequireScope(PricingConstants.Scopes.AutoPricingOverride);

        rates
            .MapPost("/auto-pricing/approve", ApproveAutoPricingAsync)
            .RequireScope(PricingConstants.Scopes.AutoPricingApprove);

        return app;
    }

    private static async Task<IResult> CalculateMarketBenchmarkAsync(
        CalculateMarketBenchmarkRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (request.IncotermId == Guid.Empty || request.PolId == Guid.Empty)
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingInvalidMarketKey",
                "Incoterm y POL son obligatorios para calcular el benchmark.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.Mode, out ShipmentMode mode))
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingInvalidMode",
                $"La modalidad '{request.Mode}' no es válida.",
                httpContext
            );
        }

        if (
            mode is ShipmentMode.Fcl or ShipmentMode.Ftl
            && (!request.ContainerTypeId.HasValue || request.ContainerTypeId == Guid.Empty)
        )
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingEquipmentRequired",
                "El equipo es obligatorio para calcular benchmark FCL/FTL.",
                httpContext
            );
        }

        if (!TryParseAmountKind(request.AmountKind, out var amountKind))
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingInvalidAmountKind",
                $"El tipo de monto '{request.AmountKind}' no es válido.",
                httpContext
            );
        }

        var targetPercentile = request.TargetPercentile ?? DefaultTargetPercentile;
        var ceilingPercentile =
            request.CompetitiveCeilingPercentile ?? DefaultCompetitiveCeilingPercentile;

        if (!ValidatePercentiles(targetPercentile, ceilingPercentile, out var percentileError))
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingInvalidPercentiles",
                percentileError!,
                httpContext
            );
        }

        var key = new MarketComparisonKey(
            request.IncotermId,
            request.PolId,
            request.PoeId,
            request.PodId,
            request.ContainerTypeId,
            mode,
            request.CarrierId
        );

        var result = await dispatcher.DispatchAsync(
            new CalculateMarketBenchmarkCommand(
                key,
                NormalizeUtc(request.ReferenceDate ?? DateTime.UtcNow),
                amountKind,
                targetPercentile,
                ceilingPercentile
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> GetRateMarketBenchmarkAsync(
        Guid rateId,
        DateTime? referenceDate,
        Guid? containerTypeId,
        string? amountKind,
        decimal? targetPercentile,
        decimal? competitiveCeilingPercentile,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseAmountKind(amountKind, out var parsedAmountKind))
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingInvalidAmountKind",
                $"El tipo de monto '{amountKind}' no es válido.",
                httpContext
            );
        }

        var target = targetPercentile ?? DefaultTargetPercentile;
        var ceiling =
            competitiveCeilingPercentile ?? DefaultCompetitiveCeilingPercentile;

        if (!ValidatePercentiles(target, ceiling, out var percentileError))
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingInvalidPercentiles",
                percentileError!,
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new GetRateMarketBenchmarkQuery(
                rateId,
                referenceDate,
                containerTypeId,
                parsedAmountKind,
                target,
                ceiling
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CalculateAutoPricingAsync(
        Guid rateId,
        CalculateAutoPricingRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseAmountKind(request.BenchmarkAmountKind, out var amountKind))
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingInvalidAmountKind",
                $"El tipo de monto '{request.BenchmarkAmountKind}' no es válido.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new CalculateAutoPricingCommand(
                rateId,
                request.ProfileCode,
                request.ReferenceDate,
                request.MinimumMarginPercentage,
                amountKind,
                request.ContainerTypeId,
                httpContext.GetCurrentUserId(),
                ResolveUserName(httpContext)
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> RecalculateAutoPricingAsync(
        Guid rateId,
        CalculateAutoPricingRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseAmountKind(request.BenchmarkAmountKind, out var amountKind))
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingInvalidAmountKind",
                $"El tipo de monto '{request.BenchmarkAmountKind}' no es válido.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new RecalculateAutoPricingCommand(
                rateId,
                request.ProfileCode,
                request.ReferenceDate,
                request.MinimumMarginPercentage,
                amountKind,
                request.ContainerTypeId,
                httpContext.GetCurrentUserId(),
                ResolveUserName(httpContext)
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> GetAutoPricingAsync(
        Guid rateId,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetAutoPricingDecisionQuery(rateId),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ApplyAutoPricingAsync(
        Guid rateId,
        ApplyAutoPricingRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (request.DecisionId == Guid.Empty)
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingDecisionRequired",
                "DecisionId es obligatorio.",
                httpContext
            );
        }

        if (!TryParseAmountKind(request.BenchmarkAmountKind, out var amountKind))
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingInvalidAmountKind",
                $"El tipo de monto '{request.BenchmarkAmountKind}' no es válido.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new ApplyAutoPricingCommand(
                rateId,
                request.DecisionId,
                request.ProfileCode,
                request.ReferenceDate,
                request.MinimumMarginPercentage,
                amountKind,
                request.ContainerTypeId,
                httpContext.GetCurrentUserId(),
                ResolveUserName(httpContext)
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> OverrideAutoPricingAsync(
        Guid rateId,
        OverrideAutoPricingRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var actorUserId = httpContext.GetCurrentUserId();
        if (!actorUserId.HasValue)
        {
            return EndpointResults.Unauthorized(
                PricingErrors.MarketPricingActorRequired.Code,
                PricingErrors.MarketPricingActorRequired.Message,
                httpContext
            );
        }

        if (request.DecisionId == Guid.Empty)
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingDecisionRequired",
                "DecisionId es obligatorio.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new OverrideAutoPricingCommand(
                rateId,
                request.DecisionId,
                request.Reason,
                (request.Details ?? Array.Empty<AutoPricingOverrideDetailRequest>())
                    .Select(x => new OverrideAutoPricingDetailCommandItem(
                        x.RateDetailId,
                        x.SaleAmount
                    ))
                    .ToArray(),
                actorUserId.Value,
                ResolveUserName(httpContext)
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ApproveAutoPricingAsync(
        Guid rateId,
        ApproveAutoPricingRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var actorUserId = httpContext.GetCurrentUserId();
        if (!actorUserId.HasValue)
        {
            return EndpointResults.Unauthorized(
                PricingErrors.MarketPricingActorRequired.Code,
                PricingErrors.MarketPricingActorRequired.Message,
                httpContext
            );
        }

        if (request.DecisionId == Guid.Empty)
        {
            return EndpointResults.BadRequest(
                "Pricing.MarketPricingDecisionRequired",
                "DecisionId es obligatorio.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new ApproveAutoPricingCommand(
                rateId,
                request.DecisionId,
                actorUserId.Value,
                ResolveUserName(httpContext)
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static bool TryParseAmountKind(
        string? value,
        out MarketBenchmarkAmountKind amountKind
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            amountKind = MarketBenchmarkAmountKind.AllIn;
            return true;
        }

        return TryParseDefinedEnum(value, out amountKind);
    }

    private static bool ValidatePercentiles(
        decimal target,
        decimal ceiling,
        out string? error
    )
    {
        if (target < 0m || target > 100m || ceiling < 0m || ceiling > 100m)
        {
            error = "Los percentiles deben estar entre 0 y 100.";
            return false;
        }

        if (ceiling < target)
        {
            error = "El percentil de techo competitivo no puede ser menor que el target.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseDefinedEnum<TEnum>(
        string? value,
        out TEnum parsed
    )
        where TEnum : struct, Enum
    {
        if (
            Enum.TryParse(value?.Trim(), ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed)
        )
        {
            return true;
        }

        parsed = default;
        return false;
    }

    private static string? ResolveUserName(HttpContext httpContext) =>
        httpContext.User.Identity?.Name
        ?? httpContext.User.FindFirst("name")?.Value
        ?? httpContext.User.FindFirst("preferred_username")?.Value;

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}

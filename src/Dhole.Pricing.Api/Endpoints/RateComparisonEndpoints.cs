using System.Security.Claims;
using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Rates.CreateRate;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateComparisonEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapRateComparisonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rate-comparisons")
            .WithTags("Rate comparisons")
            .RequireAuthorization();

        group.MapGet("/", GetAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/{id:guid}", GetByIdAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapPost("/{id:guid}/create-rate", CreateRateAsync)
            .RequireScope(PricingConstants.Scopes.RateCreate)
            .RequireScope(PricingConstants.Scopes.ImportFclRateCreateAsRate);
        group.MapPost("/{id:guid}/dismiss", DismissAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        return app;
    }

    private static async Task<IResult> GetAsync(
        string? status,
        int? take,
        ServiceDbContext db,
        CancellationToken ct
    )
    {
        RateComparisonStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RateComparisonStatus>(status, true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return Results.BadRequest(new
                {
                    code = "Pricing.InvalidRateComparisonStatus",
                    message = "El estado de comparación no es válido.",
                });
            }
            parsedStatus = parsed;
        }

        var limit = Math.Clamp(take ?? 100, 1, 250);
        var query = db.RateComparisons.AsNoTracking().AsQueryable();
        if (parsedStatus.HasValue)
            query = query.Where(x => x.Status == parsedStatus.Value);

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.SourceImportFclRateId,
                x.ComparedRateHeaderId,
                x.ComparedRateCode,
                comparisonType = x.ComparisonType.ToString(),
                status = x.Status.ToString(),
                x.PolName,
                x.PoeName,
                x.ContainerTypeName,
                x.CurrencyCode,
                x.BaselineCostAmount,
                x.BaselineSaleAmount,
                x.CandidateCostAmount,
                x.CandidateSaleAmount,
                x.BaselineComparedAmount,
                x.CandidateComparedAmount,
                x.SavingsAmount,
                x.SavingsPercent,
                x.CreatedRateHeaderId,
                x.CreatedAtUtc,
                x.ResolvedAtUtc,
            })
            .ToListAsync(ct);

        return EndpointResults.Ok(rows);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ServiceDbContext db,
        CancellationToken ct
    )
    {
        var comparison = await db.RateComparisons
            .AsNoTracking()
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (comparison is null)
            return Results.NotFound();

        var baseline = await db.RateHeaders
            .AsNoTracking()
            .Where(x => x.Id == comparison.ComparedRateHeaderId)
            .Select(x => new
            {
                x.Id,
                x.RateCode,
                x.RateName,
                status = x.Status.ToString(),
                x.ClientName,
                x.ExecutiveName,
                x.AgentName,
                x.CarrierName,
                x.PolName,
                x.PoeName,
                x.PodName,
                x.ContainerTypeName,
                x.IncotermName,
                x.TotalCostAmount,
                x.TotalSaleAmount,
                x.MarginPercentage,
                x.ValidFrom,
                x.ValidTo,
            })
            .FirstOrDefaultAsync(ct);

        return EndpointResults.Ok(new
        {
            comparison.Id,
            comparison.SourceImportFclRateId,
            comparison.ComparedRateHeaderId,
            comparison.ComparedRateCode,
            comparisonType = comparison.ComparisonType.ToString(),
            status = comparison.Status.ToString(),
            comparison.PolName,
            comparison.PoeName,
            comparison.ContainerTypeName,
            comparison.CurrencyCode,
            comparison.BaselineCostAmount,
            comparison.BaselineSaleAmount,
            comparison.CandidateCostAmount,
            comparison.CandidateSaleAmount,
            comparison.BaselineComparedAmount,
            comparison.CandidateComparedAmount,
            comparison.SavingsAmount,
            comparison.SavingsPercent,
            comparison.CreatedRateHeaderId,
            comparison.CreatedAtUtc,
            comparison.ResolvedAtUtc,
            comparison.ResolvedBy,
            baseline,
            details = comparison.Details
                .OrderBy(x => x.CostDetailType)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.CostId,
                    x.Name,
                    costDetailType = x.CostDetailType.ToString(),
                    costType = x.CostType.ToString(),
                    chargeBasis = x.ChargeBasis.ToString(),
                    x.CurrencyCode,
                    x.BaselineCostAmount,
                    x.BaselineSaleAmount,
                    x.CandidateCostAmount,
                    x.CandidateSaleAmount,
                    x.Notes,
                })
                .ToArray(),
        });
    }

    private static async Task<IResult> CreateRateAsync(
        Guid id,
        ICommandDispatcher dispatcher,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        var comparison = await db.RateComparisons
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (comparison is null)
            return Results.NotFound();
        if (comparison.Status != RateComparisonStatus.Pending)
        {
            return Results.Conflict(new
            {
                code = "Pricing.RateComparisonAlreadyResolved",
                message = "Esta comparación ya fue resuelta.",
                comparison.CreatedRateHeaderId,
            });
        }

        CreateRateCommand? storedCommand;
        try
        {
            storedCommand = JsonSerializer.Deserialize<CreateRateCommand>(
                comparison.CandidatePayloadJson,
                JsonOptions
            );
        }
        catch (JsonException)
        {
            storedCommand = null;
        }

        if (storedCommand is null)
        {
            return Results.Conflict(new
            {
                code = "Pricing.RateComparisonCandidateUnavailable",
                message = "No fue posible recuperar la tarifa automática de esta comparación.",
            });
        }

        var userId = httpContext.GetCurrentUserId();
        var command = storedCommand with
        {
            CanApproveImportedRate = HasScope(
                httpContext.User,
                PricingConstants.Scopes.ImportFclRateApprove
            ),
            CanApproveLowMargin = HasScope(
                httpContext.User,
                PricingConstants.Scopes.RateApproveLowMargin
            ),
            CreatedBy = userId,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await dispatcher.DispatchAsync(command, ct);
        if (result.IsFailure)
        {
            await transaction.RollbackAsync(ct);
            return EndpointResults.FromResult(result, httpContext);
        }

        comparison.MarkCreated(result.Value, userId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DismissAsync(
        Guid id,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        var comparison = await db.RateComparisons.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (comparison is null)
            return Results.NotFound();
        if (comparison.Status != RateComparisonStatus.Pending)
        {
            return Results.Conflict(new
            {
                code = "Pricing.RateComparisonAlreadyResolved",
                message = "Esta comparación ya fue resuelta.",
            });
        }

        comparison.Dismiss(httpContext.GetCurrentUserId());
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
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
}

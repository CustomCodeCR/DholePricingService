using CustomCodeFramework.Core.Pagination;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Contracts.Competitors;
using Dhole.Pricing.Domain.Competitors.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class CompetitorTariffEndpoints
{
    public static IEndpointRouteBuilder MapCompetitorTariffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/pricing/competitor-tariffs")
            .WithTags("Competitor tariffs")
            .RequireAuthorization();

        group.MapGet("/", BrowseAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/matching", GetMatchingAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/{competitorTariffId:guid}", GetByIdAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapPost("/", CreateAsync).RequireScope(PricingConstants.Scopes.RateCreate);
        group.MapPut("/{competitorTariffId:guid}", UpdateAsync).RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapDelete("/{competitorTariffId:guid}", DeleteAsync).RequireScope(PricingConstants.Scopes.RateDelete);

        return app;
    }

    private static async Task<IResult> BrowseAsync(
        int? pageNumber,
        int? pageSize,
        string[]? polId,
        string[]? poeId,
        string[]? podId,
        string[]? carrierId,
        string[]? shipmentMode,
        DateTime? validOn,
        ServiceDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var page = PageRequest.Create(
            Math.Max(1, pageNumber ?? 1),
            Math.Clamp(pageSize ?? 20, 1, 100)
        );

        var polIds = ParseGuidList(polId);
        var poeIds = ParseGuidList(poeId);
        var podIds = ParseGuidList(podId);
        var carrierIds = ParseGuidList(carrierId);
        var shipmentModes = ParseEnumList<ShipmentMode>(shipmentMode);

        IQueryable<CompetitorTariff> query = dbContext.CompetitorTariffs.AsNoTracking();

        if (polIds.Length > 0)
            query = query.Where(x => x.PolIds.Any(id => polIds.Contains(id)));

        if (poeIds.Length > 0)
            query = query.Where(x => x.PoeIds.Any(id => poeIds.Contains(id)));

        if (podIds.Length > 0)
            query = query.Where(x => x.PodIds.Any(id => podIds.Contains(id)));

        if (carrierIds.Length > 0)
            query = query.Where(x => x.CarrierIds.Any(id => carrierIds.Contains(id)));

        if (shipmentModes.Length > 0)
            query = query.Where(x => shipmentModes.Contains(x.ShipmentMode));

        if (validOn.HasValue)
        {
            var moment = NormalizeUtc(validOn.Value);
            query = query.Where(x => x.ValidFrom <= moment && x.ValidTo >= moment);
        }

        var total = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderByDescending(x => x.ValidTo)
            .ThenByDescending(x => x.ValidFrom)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return EndpointResults.FromPaged(
            PagedResult<CompetitorTariffDto>.Create(
                entities.Select(Map).ToArray(),
                page.PageNumber,
                page.PageSize,
                total
            )
        );
    }

    private static async Task<IResult> GetMatchingAsync(
        Guid polId,
        Guid poeId,
        Guid podId,
        Guid carrierId,
        string shipmentMode,
        DateTime? validOn,
        ServiceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (
            polId == Guid.Empty ||
            poeId == Guid.Empty ||
            podId == Guid.Empty ||
            carrierId == Guid.Empty
        )
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidRoute",
                "POL, POE, POD y naviera son obligatorios para buscar tarifas de la competencia.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(shipmentMode, out ShipmentMode mode))
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidShipmentMode",
                "La modalidad indicada no es válida.",
                httpContext
            );
        }

        var moment = NormalizeUtc(validOn ?? DateTime.UtcNow);

        // La comparación de competencia debe responder por contexto comercial de la
        // tarifa (ruta + naviera + modalidad). La vigencia no debe ocultar un
        // tarifario histórico o futuro: se usa únicamente para priorizar primero
        // los documentos vigentes en la fecha consultada, conservando el resto para
        // análisis y referencia comercial.
        var entities = await dbContext
            .CompetitorTariffs
            .AsNoTracking()
            .Where(x =>
                x.PolIds.Contains(polId) &&
                x.PoeIds.Contains(poeId) &&
                x.PodIds.Contains(podId) &&
                x.CarrierIds.Contains(carrierId) &&
                x.ShipmentMode == mode
            )
            .ToListAsync(cancellationToken);

        var ordered = entities
            .OrderByDescending(x => x.ValidFrom <= moment && x.ValidTo >= moment)
            .ThenByDescending(x => x.ValidTo)
            .ThenByDescending(x => x.ValidFrom)
            .Select(Map)
            .ToArray();

        return EndpointResults.Ok(ordered);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid competitorTariffId,
        ServiceDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var entity = await dbContext
            .CompetitorTariffs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == competitorTariffId, cancellationToken);

        return entity is null
            ? Results.NotFound()
            : EndpointResults.Ok(Map(entity));
    }

    private static async Task<IResult> CreateAsync(
        UpsertCompetitorTariffRequest request,
        ServiceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.ShipmentMode, out ShipmentMode shipmentMode))
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidShipmentMode",
                "La modalidad indicada no es válida.",
                httpContext
            );
        }

        var id = request.Id.GetValueOrDefault();
        if (id == Guid.Empty)
            id = Guid.NewGuid();

        if (await dbContext.CompetitorTariffs.AnyAsync(x => x.Id == id, cancellationToken))
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.AlreadyExists",
                "Ya existe un tarifario de competencia con ese identificador.",
                httpContext
            );
        }

        try
        {
            var entity = CompetitorTariff.Create(
                id,
                request.PolIds,
                request.PoeIds,
                request.PodIds,
                request.CarrierIds,
                request.ValidFrom,
                request.ValidTo,
                shipmentMode,
                request.StorageId
            );

            dbContext.CompetitorTariffs.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return EndpointResults.Ok(entity.Id);
        }
        catch (InvalidOperationException exception)
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.Invalid",
                exception.Message,
                httpContext
            );
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid competitorTariffId,
        UpsertCompetitorTariffRequest request,
        ServiceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var entity = await dbContext
            .CompetitorTariffs
            .FirstOrDefaultAsync(x => x.Id == competitorTariffId, cancellationToken);

        if (entity is null)
            return Results.NotFound();

        if (!TryParseDefinedEnum(request.ShipmentMode, out ShipmentMode shipmentMode))
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidShipmentMode",
                "La modalidad indicada no es válida.",
                httpContext
            );
        }

        try
        {
            entity.Update(
                request.PolIds,
                request.PoeIds,
                request.PodIds,
                request.CarrierIds,
                request.ValidFrom,
                request.ValidTo,
                shipmentMode,
                request.StorageId
            );

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.Invalid",
                exception.Message,
                httpContext
            );
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid competitorTariffId,
        ServiceDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var entity = await dbContext
            .CompetitorTariffs
            .FirstOrDefaultAsync(x => x.Id == competitorTariffId, cancellationToken);

        if (entity is null)
            return Results.NotFound();

        dbContext.CompetitorTariffs.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static CompetitorTariffDto Map(CompetitorTariff entity) =>
        new(
            entity.Id,
            entity.PolIds,
            entity.PoeIds,
            entity.PodIds,
            entity.CarrierIds,
            entity.ValidFrom,
            entity.ValidTo,
            entity.ShipmentMode.ToString(),
            entity.StorageId
        );

    private static Guid[] ParseGuidList(IEnumerable<string>? values) =>
        SplitMultiValues(values)
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

    private static TEnum[] ParseEnumList<TEnum>(IEnumerable<string>? values)
        where TEnum : struct, Enum =>
        SplitMultiValues(values)
            .Select(value => TryParseDefinedEnum(value, out TEnum item) ? item : (TEnum?)null)
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .Distinct()
            .ToArray();

    private static IEnumerable<string> SplitMultiValues(IEnumerable<string>? values) =>
        (values ?? [])
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value));

    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        if (
            Enum.TryParse(value?.Trim(), ignoreCase: true, out parsed) &&
            Enum.IsDefined(parsed)
        )
        {
            return true;
        }

        parsed = default;
        return false;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}

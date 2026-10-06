using System.Security.Cryptography;
using System.Text.Json;
using CustomCodeFramework.Core.Pagination;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.MarketPricing.Normalization;
using Dhole.Pricing.Contracts.Competitors;
using Dhole.Pricing.Domain.Competitors.Entities;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
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
        group.MapPost("/import", ImportAsync).RequireScope(PricingConstants.Scopes.RateCreate);
        group.MapGet("/{competitorTariffId:guid}/observations", GetObservationsAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/{competitorTariffId:guid}", GetByIdAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapPost("/", CreateAsync).RequireIdempotency().RequireScope(PricingConstants.Scopes.RateCreate);
        group.MapPut("/{competitorTariffId:guid}", UpdateAsync).RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapDelete("/{competitorTariffId:guid}", DeleteAsync).RequireScope(PricingConstants.Scopes.RateDelete);

        return app;
    }

    private static async Task<IResult> BrowseAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        string[]? importStatus,
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
        var statuses = SplitMultiValues(importStatus)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IQueryable<CompetitorTariff> query = dbContext.CompetitorTariffs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.CompetitorCompanyName, pattern)
                || (x.OriginalFileName != null && EF.Functions.ILike(x.OriginalFileName, pattern))
            );
        }

        if (statuses.Length > 0)
            query = query.Where(x => statuses.Contains(x.ImportStatus));

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
            .OrderByDescending(x => x.ImportedAtUtc)
            .ThenByDescending(x => x.ValidTo)
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

    private static async Task<IResult> ImportAsync(
        HttpRequest request,
        ServiceDbContext dbContext,
        ICompetitorTariffImportQueue importQueue,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!request.HasFormContentType)
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidImport",
                "La importación debe enviarse como multipart/form-data.",
                httpContext
            );
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");

        if (file is null || file.Length <= 0)
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.FileRequired",
                "Adjunte el tarifario de la competencia.",
                httpContext
            );
        }

        if (!Guid.TryParse(form["id"].ToString(), out var id) || id == Guid.Empty)
            id = Guid.NewGuid();

        if (!Guid.TryParse(form["storageId"].ToString(), out var storageId) || storageId == Guid.Empty)
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.StorageRequired",
                "StorageId es obligatorio para conservar el archivo fuente.",
                httpContext
            );
        }

        if (!Guid.TryParse(form["incotermId"].ToString(), out var incotermId) || incotermId == Guid.Empty)
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.IncotermRequired",
                "Seleccione el Incoterm del tarifario para poder alimentar Average.",
                httpContext
            );
        }

        var competitorCompanyName = form["competitorCompanyName"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(competitorCompanyName))
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.CompetitorRequired",
                "Indique el competidor o fuente del tarifario.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(form["shipmentMode"].ToString(), out ShipmentMode shipmentMode))
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidShipmentMode",
                "La modalidad indicada no es válida.",
                httpContext
            );
        }

        if (!TryParseOptionalUtcDate(form["validFrom"].ToString(), out var fallbackFrom)
            || !TryParseOptionalUtcDate(form["validTo"].ToString(), out var fallbackTo))
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidValidity",
                "La vigencia general indicada no es válida.",
                httpContext
            );
        }

        if (fallbackFrom.HasValue && fallbackTo.HasValue && fallbackTo < fallbackFrom)
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidValidity",
                "La vigencia hasta no puede ser anterior a la vigencia desde.",
                httpContext
            );
        }

        if (await dbContext.CompetitorTariffs.AnyAsync(x => x.Id == id, cancellationToken))
        {
            return EndpointResults.BadRequest(
                "CompetitorTariff.AlreadyExists",
                "Ya existe un tarifario de competencia con ese identificador.",
                httpContext
            );
        }

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        var initialFrom = fallbackFrom ?? DateTime.UtcNow;
        var initialTo = fallbackTo ?? fallbackFrom ?? initialFrom;

        var entity = CompetitorTariff.CreateImport(
            id,
            competitorCompanyName,
            incotermId,
            initialFrom,
            initialTo,
            shipmentMode,
            storageId,
            file.FileName
        );

        dbContext.CompetitorTariffs.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await importQueue.QueueAsync(
            new CompetitorTariffImportWorkItem(
                id,
                incotermId,
                competitorCompanyName,
                shipmentMode,
                fallbackFrom,
                fallbackTo,
                file.FileName,
                file.ContentType,
                Path.GetExtension(file.FileName),
                file.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                httpContext.GetCurrentUserId(),
                ResolveUserName(httpContext),
                bytes
            ),
            cancellationToken
        );

        return EndpointResults.Ok(Map(entity));
    }

    private static async Task<IResult> GetObservationsAsync(
        Guid competitorTariffId,
        ServiceDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var exists = await dbContext.CompetitorTariffs
            .AsNoTracking()
            .AnyAsync(x => x.Id == competitorTariffId, cancellationToken);

        if (!exists)
            return Results.NotFound();

        var rows = await dbContext.CompetitorRateObservations
            .AsNoTracking()
            .Where(x => x.CompetitorTariffId == competitorTariffId)
            .OrderByDescending(x => x.ValidTo)
            .ThenBy(x => x.PolName)
            .ThenBy(x => x.CarrierName)
            .ToListAsync(cancellationToken);

        return EndpointResults.Ok(rows.Select(MapObservation).ToArray());
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

        var observations = await dbContext.CompetitorRateObservations
            .Where(x => x.CompetitorTariffId == competitorTariffId)
            .ToListAsync(cancellationToken);

        if (observations.Count > 0)
            dbContext.CompetitorRateObservations.RemoveRange(observations);

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
            entity.StorageId,
            entity.CompetitorCompanyName,
            entity.IncotermId,
            entity.OriginalFileName,
            entity.ExtractionExecutionId,
            entity.ObservationCount,
            entity.ReviewCount,
            entity.ImportStatus,
            entity.ImportedAtUtc
        );

    private static CompetitorRateObservationDto MapObservation(
        CompetitorRateObservation entity
    )
    {
        var normalizedAmount = entity.NormalizedAllIn
            ?? entity.NormalizedAmount
            ?? entity.NormalizedOceanFreight;
        var usable = IsUsableObservation(entity);

        return new CompetitorRateObservationDto(
            entity.Id,
            entity.CompetitorTariffId ?? Guid.Empty,
            entity.CompetitorCompanyName,
            entity.IncotermId,
            entity.IncotermCode,
            entity.PolId,
            entity.PolName,
            entity.PolCode,
            entity.PoeId,
            entity.PoeName,
            entity.PoeCode,
            entity.PodId,
            entity.PodName,
            entity.PodCode,
            entity.CarrierId,
            entity.CarrierName,
            entity.CarrierCode,
            entity.ContainerTypeId,
            entity.ContainerTypeCode,
            entity.Mode.ToString(),
            entity.Currency,
            entity.OriginalAmount ?? SumRawComponents(entity),
            entity.NormalizedCurrency,
            normalizedAmount,
            entity.ValidFrom,
            entity.ValidTo,
            entity.ExtractionConfidence,
            entity.NormalizationConfidence,
            usable,
            !usable
        );
    }

    private static bool IsUsableObservation(CompetitorRateObservation observation)
    {
        var normalizedAmount = observation.NormalizedAllIn
            ?? observation.NormalizedAmount
            ?? observation.NormalizedOceanFreight;

        if (
            !observation.IncotermId.HasValue
            || !observation.PolId.HasValue
            || !normalizedAmount.HasValue
            || !string.Equals(
                observation.NormalizedCurrency,
                "USD",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        return observation.Mode is not (ShipmentMode.Fcl or ShipmentMode.Ftl)
            || observation.ContainerTypeId.HasValue;
    }

    private static MarketRateBasis ResolveRateBasis(ShipmentMode mode) =>
        mode switch
        {
            ShipmentMode.Fcl => MarketRateBasis.PerContainer,
            ShipmentMode.Ftl => MarketRateBasis.PerTruck,
            ShipmentMode.Lcl => MarketRateBasis.WeightOrMeasure,
            ShipmentMode.Ltl => MarketRateBasis.PerShipment,
            ShipmentMode.Air or ShipmentMode.AirConsol => MarketRateBasis.PerKg,
            _ => MarketRateBasis.Unknown,
        };

    private static decimal ResolveExtractionConfidence(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "valid" or "approved" => 1m,
            "warning" or "review" or "pending" => 0.85m,
            _ => 0.65m,
        };

    private static decimal? SumRawComponents(DataExtractionFclPricingRow row)
    {
        var values = new[]
        {
            row.OceanFreight,
            row.OriginCharges,
            row.DestinationCharges,
            row.Surcharges,
        };

        return values.Any(x => x.HasValue)
            ? values.Where(x => x.HasValue).Sum(x => x!.Value)
            : null;
    }

    private static decimal? SumRawComponents(CompetitorRateObservation observation)
    {
        var values = new[]
        {
            observation.OceanFreight,
            observation.OriginCharges,
            observation.DestinationCharges,
            observation.InlandCharges,
            observation.OtherCharges,
        };

        return values.Any(x => x.HasValue)
            ? values.Where(x => x.HasValue).Sum(x => x!.Value)
            : null;
    }

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

    private static bool TryParseOptionalUtcDate(string? value, out DateTime? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!DateTime.TryParse(value, out var date))
            return false;

        parsed = NormalizeUtc(date);
        return true;
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

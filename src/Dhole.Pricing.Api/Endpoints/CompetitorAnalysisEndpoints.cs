using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Competitors.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Stores only report metadata here; HTML bytes are kept in Dhole Storage.
/// This is independent of competitor tariff extraction and Average calculations.
/// </summary>
public static class CompetitorAnalysisEndpoints
{
    private const long MaxHtmlBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapCompetitorAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/competitor-analyses")
            .WithTags("Competitor analysis")
            .RequireAuthorization();

        group.MapGet("/", BrowseAsync)
            .RequireScope(PricingScopeNames.CompetitorAnalysisManage);
        group.MapPost("/", CreateAsync)
            .RequireScope(PricingScopeNames.CompetitorAnalysisManage);
        group.MapPatch("/{id:guid}/validity", UpdateValidityAsync)
            .RequireScope(PricingScopeNames.CompetitorAnalysisManage);
        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireScope(PricingScopeNames.CompetitorAnalysisManage);
        return app;
    }

    private static async Task<IResult> BrowseAsync(
        ServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var documents = await dbContext.CompetitorAnalysisDocuments
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new CompetitorAnalysisDto(
                x.Id, x.StorageId, x.OriginalFileName, x.SizeInBytes, x.CreatedAtUtc, x.ValidFromUtc, x.ValidToUtc))
            .Take(200)
            .ToListAsync(cancellationToken);
        return EndpointResults.Ok(documents);
    }

    private static async Task<IResult> CreateAsync(
        CreateCompetitorAnalysisRequest request,
        ServiceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var fileName = request.OriginalFileName?.Trim() ?? "";
        if (request.Id == Guid.Empty || request.StorageId == Guid.Empty ||
            fileName.Length is < 5 or > 255 ||
            !(fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
              || fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)) ||
            fileName.Contains('/') || fileName.Contains('\\') ||
            request.SizeInBytes is <= 0 or > MaxHtmlBytes || !ValidDates(request.ValidFromUtc, request.ValidToUtc))
        {
            return EndpointResults.BadRequest(
                "CompetitorAnalysis.InvalidFile",
                "Indique un HTML válido de máximo 10 MB, previamente almacenado en Storage.",
                httpContext);
        }

        var existing = await dbContext.CompetitorAnalysisDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StorageId == request.StorageId, cancellationToken);
        if (existing is not null)
            return EndpointResults.Ok(ToDto(existing));

        var document = new CompetitorAnalysisDocument
        {
            Id = request.Id,
            StorageId = request.StorageId,
            OriginalFileName = fileName,
            SizeInBytes = request.SizeInBytes,
            CreatedAtUtc = DateTime.UtcNow,
            ValidFromUtc = request.ValidFromUtc,
            ValidToUtc = request.ValidToUtc,
        };
        dbContext.CompetitorAnalysisDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        return EndpointResults.Ok(ToDto(document));
    }

    private static bool ValidDates(DateTime? from, DateTime? to) =>
        (!from.HasValue || from.Value.Kind == DateTimeKind.Utc) &&
        (!to.HasValue || to.Value.Kind == DateTimeKind.Utc) &&
        (!from.HasValue || !to.HasValue || from <= to);

    private static async Task<IResult> UpdateValidityAsync(
        Guid id, UpdateCompetitorAnalysisValidityRequest request,
        ServiceDbContext dbContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!ValidDates(request.ValidFromUtc, request.ValidToUtc))
            return EndpointResults.BadRequest("CompetitorAnalysis.InvalidValidity", "La vigencia debe estar en UTC y la fecha final no puede ser anterior a la inicial.", httpContext);
        var document = await dbContext.CompetitorAnalysisDocuments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (document is null) return Results.NotFound();
        document.ValidFromUtc = request.ValidFromUtc;
        document.ValidToUtc = request.ValidToUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return EndpointResults.Ok(ToDto(document));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ServiceDbContext dbContext, CancellationToken cancellationToken)
    {
        var document = await dbContext.CompetitorAnalysisDocuments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (document is null) return Results.NotFound();
        dbContext.CompetitorAnalysisDocuments.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        // Remove the report registration. Storage retains its file for independent lifecycle management.
        return Results.NoContent();
    }

    private static CompetitorAnalysisDto ToDto(CompetitorAnalysisDocument d) =>
        new(d.Id, d.StorageId, d.OriginalFileName, d.SizeInBytes, d.CreatedAtUtc, d.ValidFromUtc, d.ValidToUtc);
}

public sealed record CreateCompetitorAnalysisRequest(
    Guid Id, Guid StorageId, string OriginalFileName, long SizeInBytes, DateTime? ValidFromUtc, DateTime? ValidToUtc);

public sealed record CompetitorAnalysisDto(
    Guid Id, Guid StorageId, string OriginalFileName, long SizeInBytes, DateTime CreatedAtUtc, DateTime? ValidFromUtc, DateTime? ValidToUtc);

public sealed record UpdateCompetitorAnalysisValidityRequest(DateTime? ValidFromUtc, DateTime? ValidToUtc);

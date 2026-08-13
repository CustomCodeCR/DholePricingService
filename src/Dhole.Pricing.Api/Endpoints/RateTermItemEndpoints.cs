using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Contracts.Rates.Request;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateTermItemEndpoints
{
    public static IEndpointRouteBuilder MapRateTermItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rate-term-items").WithTags("Rate terms").RequireAuthorization();
        group.MapGet("/", GetAsync).RequireScope(PricingConstants.Scopes.RateTermView);
        group.MapGet("/select", GetSelectAsync).RequireScope(PricingConstants.Scopes.RateTermSelect);
        group.MapPost("/", CreateAsync).RequireScope(PricingConstants.Scopes.RateTermCreate);
        group.MapPut("/{id:guid}", UpdateAsync).RequireScope(PricingConstants.Scopes.RateTermUpdate);
        group.MapPatch("/{id:guid}/set-active", SetActiveAsync).RequireScope(PricingConstants.Scopes.RateTermSetActive);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireScope(PricingConstants.Scopes.RateTermDelete);
        return app;
    }

    private static async Task<IResult> GetAsync(bool? isActive, ServiceDbContext db, CancellationToken ct)
    {
        var query = db.RateTermItems.AsNoTracking().AsQueryable();
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        var items = await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Text)
            .Select(x => new RateTermItemDto(x.Id, x.Text, x.SortOrder, x.IsActive)).ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetSelectAsync(ServiceDbContext db, CancellationToken ct)
    {
        var items = await db.RateTermItems.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Text)
            .Select(x => new RateTermItemDto(x.Id, x.Text, x.SortOrder, x.IsActive))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(CreateRateTermItemRequest request, ServiceDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Results.BadRequest(new { code = "Pricing.RateTermTextRequired", message = "El texto es requerido." });

        var normalizedText = request.Text.Trim().ToLower();
        var exists = await db.RateTermItems.AnyAsync(x => x.Text.ToLower() == normalizedText, ct);
        if (exists)
            return Results.Conflict(new { code = "Pricing.RateTermAlreadyExists", message = "El ítem ya existe en el catálogo compartido." });

        var item = RateTermItem.Create(request.Text, request.SortOrder);
        db.RateTermItems.Add(item);
        await db.SaveChangesAsync(ct);
        return Results.Ok(item.Id);
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateRateTermItemRequest request, ServiceDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Results.BadRequest(new { code = "Pricing.RateTermTextRequired", message = "El texto es requerido." });

        var item = await db.RateTermItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();

        var normalizedText = request.Text.Trim().ToLower();
        var exists = await db.RateTermItems.AnyAsync(
            x => x.Id != id && x.Text.ToLower() == normalizedText,
            ct
        );
        if (exists)
            return Results.Conflict(new { code = "Pricing.RateTermAlreadyExists", message = "El ítem ya existe en el catálogo compartido." });

        item.Update(request.Text, request.SortOrder, request.IsActive);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetActiveAsync(Guid id, SetRateTermItemActiveRequest request, ServiceDbContext db, CancellationToken ct)
    {
        var item = await db.RateTermItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        item.SetActive(request.IsActive);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        var item = await db.RateTermItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        db.RateTermItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class ImportRateReviewQueueEndpoints
{
    public static IEndpointRouteBuilder MapImportRateReviewQueueEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/import-rates/review-queue", GetReviewQueueAsync)
            .WithTags("Imported FCL Rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        return app;
    }

    private static async Task<IResult> GetReviewQueueAsync(
        string? search,
        ImportSourceType? sourceType,
        ImportStatus? status,
        Guid? polId,
        Guid? poeId,
        Guid? carrierId,
        Guid? agentId,
        Guid? containerTypeId,
        Guid? importBatchId,
        DateTime? createdFrom,
        DateTime? createdTo,
        int? pageNumber,
        int? pageSize,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.ImportFclRates
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status != ImportStatus.Expired);

        if (sourceType.HasValue)
            query = query.Where(x => x.SourceType == sourceType.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        if (polId.HasValue)
            query = query.Where(x => x.PolId == polId.Value);

        if (poeId.HasValue)
            query = query.Where(x => x.PoeId == poeId.Value);

        if (carrierId.HasValue)
            query = query.Where(x => x.CarrierId == carrierId.Value);

        if (agentId.HasValue)
            query = query.Where(x => x.AgentId == agentId.Value);

        if (containerTypeId.HasValue)
            query = query.Where(x => x.ContainerTypeId == containerTypeId.Value);

        if (importBatchId.HasValue)
            query = query.Where(x => x.ImportBatchId == importBatchId.Value);

        if (createdFrom.HasValue)
        {
            var from = DateTime.SpecifyKind(createdFrom.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAtUtc >= from);
        }

        if (createdTo.HasValue)
        {
            var until = DateTime.SpecifyKind(createdTo.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAtUtc < until);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.CarrierName.ToLower().Contains(value)
                || x.AgentName.ToLower().Contains(value)
                || x.PolName.ToLower().Contains(value)
                || x.PoeName.ToLower().Contains(value)
                || x.PodName.ToLower().Contains(value)
                || x.ContainerTypeName.ToLower().Contains(value)
                || (x.Commodity != null && x.Commodity.ToLower().Contains(value))
            );
        }

        var safePageSize = Math.Clamp(pageSize ?? 25, 10, 100);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize));
        var safePageNumber = Math.Clamp(pageNumber ?? 1, 1, totalPages);

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((safePageNumber - 1) * safePageSize)
            .Take(safePageSize)
            .Select(x => new ImportRateReviewQueueItemDto(
                x.Id,
                x.ImportBatchId,
                x.SourceType.ToString(),
                x.CarrierName,
                x.AgentName,
                x.PolName,
                x.PoeName,
                x.PodName,
                x.ContainerTypeName,
                x.CurrencyName,
                x.OceanFreight ?? x.Freight,
                x.ValidFrom,
                x.ValidTo,
                x.Status.ToString(),
                x.SpaceComment,
                x.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        return Results.Ok(new ImportRateReviewQueueResponseDto(
            rows,
            safePageNumber,
            safePageSize,
            totalCount,
            totalPages
        ));
    }

    private sealed record ImportRateReviewQueueResponseDto(
        IReadOnlyCollection<ImportRateReviewQueueItemDto> Items,
        int PageNumber,
        int PageSize,
        int TotalCount,
        int TotalPages
    );

    private sealed record ImportRateReviewQueueItemDto(
        Guid Id,
        Guid ImportBatchId,
        string SourceType,
        string Carrier,
        string Agent,
        string Pol,
        string Poe,
        string Pod,
        string ContainerType,
        string Currency,
        decimal Freight,
        DateTime ValidFrom,
        DateTime ValidTo,
        string Status,
        string? SpaceComment,
        DateTime CreatedAt
    );
}

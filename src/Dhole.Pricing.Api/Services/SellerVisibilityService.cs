using System.Data;
using System.Security.Claims;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

public enum SellerVisibilityMode
{
    Own,
    Selected,
    All,
}

public sealed record SellerVisibilityContext(
    Guid ViewerUserId,
    SellerVisibilityMode Mode,
    IReadOnlySet<Guid> SellerUserIds
);

public sealed class SellerVisibilityService(ServiceDbContext db)
{
    public bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        var scopes = user.Claims
            .Where(claim =>
                claim.Type.Equals("scp", StringComparison.OrdinalIgnoreCase)
                || claim.Type.Equals("scope", StringComparison.OrdinalIgnoreCase)
                || claim.Type.Equals("scopes", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => Split(claim.Value))
            .Select(value => value.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalized = requiredScope.Trim().ToLowerInvariant();
        if (scopes.Contains("*") || scopes.Contains(normalized))
        {
            return true;
        }

        return scopes.Any(scope =>
            scope.EndsWith(".*", StringComparison.Ordinal)
            && normalized.StartsWith(scope[..^1], StringComparison.Ordinal));
    }

    public async Task<SellerVisibilityContext?> ResolveAsync(
        HttpContext context,
        bool requireElevated,
        CancellationToken cancellationToken)
    {
        var viewerUserId = context.GetCurrentUserId();
        if (!viewerUserId.HasValue || viewerUserId.Value == Guid.Empty)
        {
            return null;
        }

        if (HasScope(context.User, PricingConstants.Scopes.RateRequestViewAll))
        {
            return new SellerVisibilityContext(
                viewerUserId.Value,
                SellerVisibilityMode.All,
                new HashSet<Guid>()
            );
        }

        if (HasScope(context.User, PricingConstants.Scopes.RateRequestViewSelected))
        {
            var visibleSellerIds = (await GetAssignedSellerIdsAsync(viewerUserId.Value, cancellationToken))
                .ToHashSet();
            visibleSellerIds.Add(viewerUserId.Value);

            return new SellerVisibilityContext(
                viewerUserId.Value,
                SellerVisibilityMode.Selected,
                visibleSellerIds
            );
        }

        if (!requireElevated && HasScope(context.User, PricingConstants.Scopes.RateRequestCreate))
        {
            return new SellerVisibilityContext(
                viewerUserId.Value,
                SellerVisibilityMode.Own,
                new HashSet<Guid> { viewerUserId.Value }
            );
        }

        return null;
    }

    public async Task<bool> CanViewRateAsync(
        HttpContext context,
        Guid rateId,
        CancellationToken cancellationToken)
    {
        var visibility = await ResolveAsync(context, requireElevated: false, cancellationToken);
        if (visibility is null)
        {
            return false;
        }

        if (visibility.Mode == SellerVisibilityMode.All)
        {
            return true;
        }

        var sellerUserId = await db.RateRequests
            .AsNoTracking()
            .Where(request => request.RateId == rateId)
            .OrderByDescending(request => request.RequestedAtUtc)
            .Select(request => (Guid?)request.SellerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        return sellerUserId.HasValue && visibility.SellerUserIds.Contains(sellerUserId.Value);
    }

    public async Task<IReadOnlyList<Guid>> GetAssignedSellerIdsAsync(
        Guid viewerUserId,
        CancellationToken cancellationToken)
    {
        var result = new List<Guid>();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT seller_user_id
                FROM pricing."SellerVisibilityRules"
                WHERE viewer_user_id = @viewerUserId
                ORDER BY seller_user_id;
                """;

            var viewerParameter = command.CreateParameter();
            viewerParameter.ParameterName = "@viewerUserId";
            viewerParameter.Value = viewerUserId;
            command.Parameters.Add(viewerParameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(reader.GetGuid(0));
            }
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    public async Task ReplaceAssignedSellerIdsAsync(
        Guid viewerUserId,
        IEnumerable<Guid> sellerUserIds,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var normalizedSellerIds = sellerUserIds
            .Where(value => value != Guid.Empty && value != viewerUserId)
            .Distinct()
            .ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM pricing."SellerVisibilityRules"
            WHERE viewer_user_id = {viewerUserId};
            """,
            cancellationToken
        );

        foreach (var sellerUserId in normalizedSellerIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO pricing."SellerVisibilityRules"
                    (viewer_user_id, seller_user_id, created_at_utc, created_by_user_id)
                VALUES
                    ({viewerUserId}, {sellerUserId}, {DateTime.UtcNow}, {actorUserId})
                ON CONFLICT (viewer_user_id, seller_user_id) DO NOTHING;
                """,
                cancellationToken
            );
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static IEnumerable<string> Split(string value)
        => value.Split(
            new[] { ' ', ',', ';' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
}

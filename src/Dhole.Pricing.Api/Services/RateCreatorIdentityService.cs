using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

public sealed class RateCreatorIdentityService(
    ServiceDbContext db,
    AuthSellerDirectoryService authDirectory)
{
    public async Task<IReadOnlyList<RateDto>> EnrichAsync(
        IEnumerable<RateDto> rates,
        CancellationToken cancellationToken)
    {
        var items = rates.ToList();
        if (items.Count == 0) return items;

        var creatorIds = await LoadCreatorIdsAsync(
            items.Select(x => x.Id),
            cancellationToken
        );
        var users = await LoadUsersAsync(cancellationToken);

        return items
            .Select(rate => EnrichRate(
                rate,
                creatorIds.GetValueOrDefault(rate.Id),
                users
            ))
            .ToList();
    }

    public async Task<PricingRateDashboardDto> EnrichAsync(
        PricingRateDashboardDto dashboard,
        CancellationToken cancellationToken)
    {
        if (dashboard.RecentRates.Count == 0) return dashboard;

        var creatorIds = await LoadCreatorIdsAsync(
            dashboard.RecentRates.Select(x => x.Id),
            cancellationToken
        );
        var users = await LoadUsersAsync(cancellationToken);

        var recentRates = dashboard.RecentRates
            .Select(rate => EnrichDashboardRate(
                rate,
                creatorIds.GetValueOrDefault(rate.Id),
                users
            ))
            .ToList();

        return dashboard with { RecentRates = recentRates };
    }

    private async Task<IReadOnlyDictionary<Guid, string?>> LoadCreatorIdsAsync(
        IEnumerable<Guid> rateIds,
        CancellationToken cancellationToken)
    {
        var ids = rateIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, string?>();

        return await db.RateHeaders
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.CreatedBy })
            .ToDictionaryAsync(
                x => x.Id,
                x => x.CreatedBy,
                cancellationToken
            );
    }

    private async Task<IReadOnlyDictionary<string, SellerDirectoryUser>> LoadUsersAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var users = await authDirectory.GetPricingUsersAsync(cancellationToken);
            return users.ToDictionary(
                x => x.UserId.ToString("D"),
                StringComparer.OrdinalIgnoreCase
            );
        }
        catch
        {
            // El identificador de auditoría se conserva aunque Auth no esté disponible.
            return new Dictionary<string, SellerDirectoryUser>(
                StringComparer.OrdinalIgnoreCase
            );
        }
    }

    private static RateDto EnrichRate(
        RateDto rate,
        string? createdByUserId,
        IReadOnlyDictionary<string, SellerDirectoryUser> users)
    {
        var user = ResolveUser(createdByUserId, users);
        return rate with
        {
            CreatedByUserId = createdByUserId,
            CreatedByUserName = user?.UserName,
            CreatedByDisplayName = DisplayName(user)
        };
    }

    private static PricingRateDashboardItemDto EnrichDashboardRate(
        PricingRateDashboardItemDto rate,
        string? createdByUserId,
        IReadOnlyDictionary<string, SellerDirectoryUser> users)
    {
        var user = ResolveUser(createdByUserId, users);
        return rate with
        {
            CreatedByUserId = createdByUserId,
            CreatedByUserName = user?.UserName,
            CreatedByDisplayName = DisplayName(user)
        };
    }

    private static SellerDirectoryUser? ResolveUser(
        string? createdByUserId,
        IReadOnlyDictionary<string, SellerDirectoryUser> users)
    {
        if (string.IsNullOrWhiteSpace(createdByUserId)) return null;
        return users.TryGetValue(createdByUserId.Trim(), out var user)
            ? user
            : null;
    }

    private static string? DisplayName(SellerDirectoryUser? user)
    {
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            return user.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(user.UserName))
            return user.UserName.Trim();
        return string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim();
    }
}

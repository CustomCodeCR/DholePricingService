using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

// Completa las identidades de auditoría con nombres de Auth. "Revisado por"
// proviene de una revisión de contenido; "Última modificación por" refleja
// la última actualización registrada, que también puede ser un cambio de estado.
public sealed class RateCreatorIdentityService(
    ServiceDbContext db,
    AuthSellerDirectoryService authDirectory)
{
    private sealed record RateActors(string? CreatedBy, string? UpdatedBy, string? ReviewedBy);

    public async Task<IReadOnlyList<RateDto>> EnrichAsync(
        IEnumerable<RateDto> rates,
        CancellationToken cancellationToken)
    {
        var items = rates.ToList();
        if (items.Count == 0) return items;

        var actors = await LoadActorsAsync(items.Select(x => x.Id), cancellationToken);
        var users = await LoadUsersAsync(cancellationToken);

        return items
            .Select(rate => EnrichRate(
                rate,
                actors.GetValueOrDefault(rate.Id),
                users
            ))
            .ToList();
    }

    public async Task<PricingRateDashboardDto> EnrichAsync(
        PricingRateDashboardDto dashboard,
        CancellationToken cancellationToken)
    {
        if (dashboard.RecentRates.Count == 0) return dashboard;

        var actors = await LoadActorsAsync(
            dashboard.RecentRates.Select(x => x.Id),
            cancellationToken
        );
        var users = await LoadUsersAsync(cancellationToken);

        var recentRates = dashboard.RecentRates
            .Select(rate => EnrichDashboardRate(
                rate,
                actors.GetValueOrDefault(rate.Id),
                users
            ))
            .ToList();

        return dashboard with { RecentRates = recentRates };
    }

    private async Task<IReadOnlyDictionary<Guid, RateActors>> LoadActorsAsync(
        IEnumerable<Guid> rateIds,
        CancellationToken cancellationToken)
    {
        var ids = rateIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, RateActors>();

        // Solo se consulta el conjunto paginado de tarifas: no hay búsquedas
        // por usuario individual ni N+1 llamadas al servicio de identidad.
        var headerActors = await db.RateHeaders
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.CreatedBy, x.UpdatedBy })
            .ToListAsync(cancellationToken);

        // Cada revisión guarda el actor de la edición. Una tarifa sin revisiones
        // no debe atribuir la revisión a su creador ni al último cambio de estado.
        var revisions = await db.Set<RateRevision>()
            .AsNoTracking()
            .Where(x => ids.Contains(x.RateHeaderId))
            .Select(x => new { x.RateHeaderId, x.RevisionNumber, x.CreatedBy })
            .ToListAsync(cancellationToken);

        var reviewers = revisions
            .GroupBy(x => x.RateHeaderId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(x => x.RevisionNumber)
                    .First()
                    .CreatedBy?.ToString("D")
            );

        return headerActors.ToDictionary(
            x => x.Id,
            x => new RateActors(
                x.CreatedBy,
                x.UpdatedBy,
                reviewers.GetValueOrDefault(x.Id)
            )
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
            // Si Auth no responde, el API conserva los IDs auditados.
            return new Dictionary<string, SellerDirectoryUser>(
                StringComparer.OrdinalIgnoreCase
            );
        }
    }

    private static RateDto EnrichRate(
        RateDto rate,
        RateActors? actors,
        IReadOnlyDictionary<string, SellerDirectoryUser> users)
    {
        var creatorId = actors?.CreatedBy ?? rate.CreatedByUserId;
        var modifierId = actors?.UpdatedBy ?? rate.LastModifiedByUserId;
        var reviewerId = actors?.ReviewedBy;
        var creator = ResolveUser(creatorId, users);
        var modifier = ResolveUser(modifierId, users);
        var reviewer = ResolveUser(reviewerId, users);

        return rate with
        {
            CreatedByUserId = creatorId,
            CreatedByUserName = creator?.UserName,
            CreatedByDisplayName = DisplayName(creator),
            LastModifiedByUserId = modifierId,
            LastModifiedByUserName = modifier?.UserName,
            LastModifiedByDisplayName = DisplayName(modifier),
            ReviewedByUserId = reviewerId,
            ReviewedByUserName = reviewer?.UserName,
            ReviewedByDisplayName = DisplayName(reviewer)
        };
    }

    private static PricingRateDashboardItemDto EnrichDashboardRate(
        PricingRateDashboardItemDto rate,
        RateActors? actors,
        IReadOnlyDictionary<string, SellerDirectoryUser> users)
    {
        var creatorId = actors?.CreatedBy ?? rate.CreatedByUserId;
        var modifierId = actors?.UpdatedBy ?? rate.LastModifiedByUserId;
        var reviewerId = actors?.ReviewedBy;
        var creator = ResolveUser(creatorId, users);
        var modifier = ResolveUser(modifierId, users);
        var reviewer = ResolveUser(reviewerId, users);

        return rate with
        {
            CreatedByUserId = creatorId,
            CreatedByUserName = creator?.UserName,
            CreatedByDisplayName = DisplayName(creator),
            LastModifiedByUserId = modifierId,
            LastModifiedByUserName = modifier?.UserName,
            LastModifiedByDisplayName = DisplayName(modifier),
            ReviewedByUserId = reviewerId,
            ReviewedByUserName = reviewer?.UserName,
            ReviewedByDisplayName = DisplayName(reviewer)
        };
    }

    private static SellerDirectoryUser? ResolveUser(
        string? userId,
        IReadOnlyDictionary<string, SellerDirectoryUser> users)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        return users.TryGetValue(userId.Trim(), out var user)
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

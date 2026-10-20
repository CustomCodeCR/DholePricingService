using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Resolves seller ownership using the authenticated Auth user, not a user-supplied name.
/// Includes rates created directly by Pricing and assigned to a sales executive.
/// </summary>
public sealed class SellerRateAccessService(
    ServiceDbContext db,
    AuthSellerDirectoryService directory)
{
    public async Task<IReadOnlyList<string>> AssignedNamesAsync(Guid userId, CancellationToken ct)
    {
        // Do not depend on the exact "Vendedor" role: commercial requests
        // can be assigned to active users with delegated/legacy roles.
        // Check aliases against the entire Auth directory so an ambiguous
        // name never grants access to another user's quotation.
        var users = await directory.GetPricingUsersAsync(ct);
        // Never allow ambiguous display names to give two users access to the same rate.
        // A quotation may store the display name or the login name of the seller.
        // Match either alias, but only if no other Auth user owns the same alias.
        var names = users
            .SelectMany(u => new[] { u.DisplayName, u.UserName }
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => new { u.UserId, Name = name!.Trim() }))
            .Where(u => !string.Equals(u.Name, "Castro Fallas", StringComparison.OrdinalIgnoreCase))
            .GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(u => u.UserId).Distinct().Count() == 1)
            .Where(group => group.First().UserId == userId)
            .Select(group => group.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return names;
    }

    public async Task<bool> IsAssignedExecutiveAsync(RateHeader rate, Guid userId, CancellationToken ct)
    {
        if (rate.IsDeleted || string.IsNullOrWhiteSpace(rate.ExecutiveName)
            || string.Equals(rate.ExecutiveName, "Castro Fallas", StringComparison.OrdinalIgnoreCase))
            return false;

        var names = await AssignedNamesAsync(userId, ct);
        return names.Contains(rate.ExecutiveName.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> CanReadAsync(RateHeader rate, Guid userId, CancellationToken ct)
    {
        var linked = await db.RateRequests.AsNoTracking()
            .AnyAsync(r => r.RateId == rate.Id && r.SellerUserId == userId, ct);
        return linked || await IsAssignedExecutiveAsync(rate, userId, ct);
    }

    public async Task<IReadOnlyCollection<Guid>> AssignedRateIdsAsync(Guid userId, CancellationToken ct)
    {
        var names = (await AssignedNamesAsync(userId, ct))
            .Select(name => name.ToLowerInvariant()).ToArray();
        if (names.Length == 0) return Array.Empty<Guid>();

        // PostgreSQL string equality is case-sensitive; align the list with the
        // case-insensitive check used by IsAssignedExecutiveAsync/GetOneAsync.
        var currentIds = await db.RateHeaders.AsNoTracking()
            .Where(rate => !rate.IsDeleted && rate.ExecutiveName != null
                && names.Contains(rate.ExecutiveName.Trim().ToLower())
                && (rate.Status != RateStatus.Open
                    && rate.Status != RateStatus.PendingApproval
                    && rate.Status != RateStatus.ApprovedByManagement
                    && rate.Status != RateStatus.RejectedByManagement
                    // An already issued quotation must remain discoverable while
                    // Pricing prepares a new revision. A seller must not see the
                    // unfinished new version; the endpoint returns the last
                    // published snapshot instead.
                    || db.Set<RateRevision>().Any(revision =>
                        revision.RateHeaderId == rate.Id
                        && (revision.Status == "Sent"
                            || revision.Status == "RequestedByClient"
                            || revision.Status == "AcceptedByClient"
                            || revision.Status == "RejectedByClient"
                            || revision.Status == "Expired"))))
            .Select(rate => rate.Id)
            .ToArrayAsync(ct);

        // Pricing may change the executive's display name on an unpublished
        // draft. Resolve the owner from the previously issued revision as well;
        // only globally unique Auth names can match.
        var priorPublished = await db.Set<RateRevision>().AsNoTracking()
            .Where(revision => (revision.Status == "Sent"
                    || revision.Status == "RequestedByClient"
                    || revision.Status == "AcceptedByClient"
                    || revision.Status == "RejectedByClient"
                    || revision.Status == "Expired")
                && db.RateHeaders.Any(rate => rate.Id == revision.RateHeaderId
                    && !rate.IsDeleted
                    && (rate.Status == RateStatus.Open
                        || rate.Status == RateStatus.PendingApproval
                        || rate.Status == RateStatus.ApprovedByManagement
                        || rate.Status == RateStatus.RejectedByManagement)))
            .Select(revision => new { revision.RateHeaderId, revision.SnapshotJson })
            .ToArrayAsync(ct);
        var oldIds = new List<Guid>();
        foreach (var published in priorPublished)
        {
            try
            {
                using var document = JsonDocument.Parse(published.SnapshotJson);
                if (!document.RootElement.TryGetProperty("ExecutiveName", out var value)
                    || value.ValueKind != JsonValueKind.String)
                    continue;
                var executiveName = value.GetString()?.Trim().ToLowerInvariant();
                if (executiveName is not null && names.Contains(executiveName))
                    oldIds.Add(published.RateHeaderId);
            }
            catch (JsonException)
            {
                // A malformed legacy snapshot is never evidence of ownership.
            }
        }

        return currentIds.Concat(oldIds).Distinct().ToArray();
    }

}

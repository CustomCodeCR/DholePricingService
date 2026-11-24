using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Messaging;

public static class OpeningNotificationIdtraGuard
{
    public static async Task<bool> HasAssignedIdtraAsync(
        ServiceDbContext db, Guid rateId, CancellationToken cancellationToken)
        => await db.RateHeaders.AsNoTracking().AnyAsync(x => x.Id == rateId
            && x.IdtraNumber != null && x.IdtraNumber.Trim() != "", cancellationToken)
        || await db.Set<RateRevision>().AsNoTracking().AnyAsync(x => x.RateHeaderId == rateId
            && x.IdtraNumber != null && x.IdtraNumber.Trim() != "", cancellationToken);

    public static bool TryGetOpeningRateId(string eventName, string payloadJson, out Guid rateId)
    {
        rateId = Guid.Empty;
        if (eventName != "notifications.notification.requested") return false;
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("notificationType", out var type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() is not ("pricing.rate.accepted.idtra-pending" or
                "pricing.rate.accepted.opening" or "pricing.rate.accepted.notice"))
            return false;
        return root.TryGetProperty("entityId", out var entityId)
            && entityId.ValueKind == JsonValueKind.String
            && Guid.TryParse(entityId.GetString(), out rateId);
    }
}

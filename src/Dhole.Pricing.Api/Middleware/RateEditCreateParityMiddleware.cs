using System.Text.Json;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Middleware;

/// <summary>
/// Makes rate editing follow the same commercial choices used by the creation wizard.
/// The previous imported source is neutralized only in the tracked entity while UpdateRate
/// runs, so legacy update-only structure locks cannot constrain a new selection. After a
/// successful update we persist the source selected by the wizard and the EXW/FCA pickup
/// location sent by Web. No explicit database transaction is opened around the HTTP pipeline.
/// </summary>
public sealed class RateEditCreateParityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ServiceDbContext db)
    {
        if (!HttpMethods.IsPut(context.Request.Method)
            || !TryGetRateId(context.Request.Path, out var rateId))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        var payload = await ReadEditPayloadAsync(context.Request, context.RequestAborted);
        context.Request.Body.Position = 0;

        if (!payload.Source.IsValid)
        {
            await WriteBadRequestAsync(
                context,
                "Pricing.InvalidSourceImportFclRateId",
                "La tarifa marítima seleccionada no tiene un identificador válido."
            );
            return;
        }

        if (!payload.Pickup.IsValid)
        {
            await WriteBadRequestAsync(
                context,
                "Pricing.InvalidPickupLocation",
                "La ubicación de recolección EXW/FCA no es válida."
            );
            return;
        }

        var rate = await db.RateHeaders
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, context.RequestAborted);

        if (rate is null)
        {
            await next(context);
            return;
        }

        var entry = db.Entry(rate);
        var sourceProperty = entry.Property(x => x.SourceImportFclRateId);
        var originalSource = rate.SourceImportFclRateId;

        // GetByIdWithDetailsAsync uses the same scoped DbContext and a tracking query.
        // Keeping this change in memory is enough for the update handler to stop treating
        // the old import association as an immutable structure lock. The normal handler
        // persists its update first; we then store the newly selected source below.
        if (payload.Source.IsPresent)
        {
            sourceProperty.CurrentValue = null;
        }

        await next(context);

        if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            // The request failed, so leave the persisted snapshot untouched. This restoration
            // is in-memory only; no SaveChanges is issued for a failed update.
            if (payload.Source.IsPresent)
            {
                sourceProperty.CurrentValue = originalSource;
            }
            return;
        }

        var needsSave = false;

        if (payload.Source.IsPresent)
        {
            sourceProperty.CurrentValue = payload.Source.Value;
            needsSave = true;
        }

        if (payload.Pickup.IsPresent)
        {
            var pickupApplies = IsPickupIncoterm(payload.IncotermName, payload.IncotermCode);
            entry.Property(x => x.PickupAddress).CurrentValue = pickupApplies
                ? Normalize(payload.Pickup.Address)
                : null;
            entry.Property(x => x.PickupLatitude).CurrentValue = pickupApplies
                ? payload.Pickup.Latitude
                : null;
            entry.Property(x => x.PickupLongitude).CurrentValue = pickupApplies
                ? payload.Pickup.Longitude
                : null;
            needsSave = true;
        }

        if (needsSave)
        {
            await db.SaveChangesAsync(context.RequestAborted);
        }
    }

    private static bool TryGetRateId(PathString path, out Guid rateId)
    {
        rateId = Guid.Empty;
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return segments.Length == 4
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("pricing", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("rates", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(segments[3], out rateId);
    }

    private static async Task<EditPayload> ReadEditPayloadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                request.Body,
                cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return EditPayload.Empty;

            var source = SourceImportValue.Missing;
            var pickup = PickupValue.Missing;
            string? incotermName = null;
            string? incotermCode = null;

            var pickupAddressPresent = false;
            var pickupLatitudePresent = false;
            var pickupLongitudePresent = false;
            string? pickupAddress = null;
            decimal? pickupLatitude = null;
            decimal? pickupLongitude = null;
            var pickupValid = true;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("sourceImportFclRateId", StringComparison.OrdinalIgnoreCase))
                {
                    source = ParseSource(property.Value);
                    continue;
                }

                if (property.Name.Equals("incotermName", StringComparison.OrdinalIgnoreCase))
                {
                    incotermName = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : null;
                    continue;
                }

                if (property.Name.Equals("incotermCode", StringComparison.OrdinalIgnoreCase))
                {
                    incotermCode = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : null;
                    continue;
                }

                if (property.Name.Equals("pickupAddress", StringComparison.OrdinalIgnoreCase))
                {
                    pickupAddressPresent = true;
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        pickupAddress = null;
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        pickupAddress = property.Value.GetString();
                    }
                    else
                    {
                        pickupValid = false;
                    }
                    continue;
                }

                if (property.Name.Equals("pickupLatitude", StringComparison.OrdinalIgnoreCase))
                {
                    pickupLatitudePresent = true;
                    pickupValid &= TryReadNullableDecimal(property.Value, out pickupLatitude);
                    continue;
                }

                if (property.Name.Equals("pickupLongitude", StringComparison.OrdinalIgnoreCase))
                {
                    pickupLongitudePresent = true;
                    pickupValid &= TryReadNullableDecimal(property.Value, out pickupLongitude);
                }
            }

            if (pickupLatitude is < -90m or > 90m || pickupLongitude is < -180m or > 180m)
            {
                pickupValid = false;
            }

            var pickupPresent = pickupAddressPresent || pickupLatitudePresent || pickupLongitudePresent;
            pickup = pickupPresent
                ? new PickupValue(true, pickupValid, pickupAddress, pickupLatitude, pickupLongitude)
                : PickupValue.Missing;

            return new EditPayload(source, pickup, incotermName, incotermCode);
        }
        catch (JsonException)
        {
            // Let the normal endpoint report malformed request bodies.
            return EditPayload.Empty;
        }
    }

    private static SourceImportValue ParseSource(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return SourceImportValue.Valid(null);

        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return SourceImportValue.Valid(null);
            if (Guid.TryParse(raw, out var id))
                return SourceImportValue.Valid(id);
        }

        return SourceImportValue.Invalid;
    }

    private static bool TryReadNullableDecimal(JsonElement value, out decimal? result)
    {
        result = null;
        if (value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var parsed))
            return false;
        result = parsed;
        return true;
    }

    private static bool IsPickupIncoterm(string? name, string? code)
    {
        static bool Matches(string? value)
        {
            var normalized = value?.Trim().ToUpperInvariant();
            return normalized is "EXW" or "FCA"
                || normalized?.StartsWith("EXW ", StringComparison.Ordinal) == true
                || normalized?.StartsWith("FCA ", StringComparison.Ordinal) == true;
        }

        return Matches(name) || Matches(code);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task WriteBadRequestAsync(
        HttpContext context,
        string code,
        string message)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { code, message }, context.RequestAborted);
    }

    private readonly record struct EditPayload(
        SourceImportValue Source,
        PickupValue Pickup,
        string? IncotermName,
        string? IncotermCode)
    {
        public static EditPayload Empty => new(
            SourceImportValue.Missing,
            PickupValue.Missing,
            null,
            null);
    }

    private readonly record struct SourceImportValue(bool IsPresent, bool IsValid, Guid? Value)
    {
        public static SourceImportValue Missing => new(false, true, null);
        public static SourceImportValue Invalid => new(true, false, null);
        public static SourceImportValue Valid(Guid? value) => new(true, true, value);
    }

    private readonly record struct PickupValue(
        bool IsPresent,
        bool IsValid,
        string? Address,
        decimal? Latitude,
        decimal? Longitude)
    {
        public static PickupValue Missing => new(false, true, null, null, null);
    }
}

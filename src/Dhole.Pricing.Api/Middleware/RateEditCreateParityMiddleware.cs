using System.Text.Json;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Middleware;

/// <summary>
/// Keeps the selected FCL import source in sync when an existing rate is edited through
/// the same wizard used for creation. The source is temporarily cleared inside a transaction
/// so legacy update-only structure locks do not prevent changing modality/equipment/provider.
/// The desired source is restored only when the normal update endpoint succeeds.
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
        var source = await ReadSourceImportAsync(context.Request, context.RequestAborted);
        context.Request.Body.Position = 0;

        if (!source.IsPresent)
        {
            await next(context);
            return;
        }

        if (!source.IsValid)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "Pricing.InvalidSourceImportFclRateId",
                message = "La tarifa marítima seleccionada no tiene un identificador válido.",
            }, context.RequestAborted);
            return;
        }

        var rate = await db.RateHeaders
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, context.RequestAborted);

        if (rate is null)
        {
            await next(context);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
        try
        {
            // UpdateRate historically locks the structure whenever the previous snapshot came
            // from an import. Editing now follows creation semantics, so the old association
            // must not constrain the new selection.
            db.Entry(rate).Property(x => x.SourceImportFclRateId).CurrentValue = null;
            await db.SaveChangesAsync(context.RequestAborted);

            await next(context);

            if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return;
            }

            db.Entry(rate).Property(x => x.SourceImportFclRateId).CurrentValue = source.Value;
            await db.SaveChangesAsync(context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
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

    private static async Task<SourceImportValue> ReadSourceImportAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                request.Body,
                cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return SourceImportValue.Missing;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("sourceImportFclRateId", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.Value.ValueKind == JsonValueKind.Null)
                    return SourceImportValue.Valid(null);

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var raw = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(raw))
                        return SourceImportValue.Valid(null);
                    if (Guid.TryParse(raw, out var id))
                        return SourceImportValue.Valid(id);
                }

                return SourceImportValue.Invalid;
            }
        }
        catch (JsonException)
        {
            // Let the normal endpoint report malformed request bodies.
        }

        return SourceImportValue.Missing;
    }

    private readonly record struct SourceImportValue(bool IsPresent, bool IsValid, Guid? Value)
    {
        public static SourceImportValue Missing => new(false, true, null);
        public static SourceImportValue Invalid => new(true, false, null);
        public static SourceImportValue Valid(Guid? value) => new(true, true, value);
    }
}

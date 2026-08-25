using System.Data;
using System.Data.Common;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class CommercialTermEndpoints
{
    public static IEndpointRouteBuilder MapCommercialTermEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/commercial-terms/resolve", ResolveAsync)
            .WithTags("Pricing rules")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);
        return app;
    }

    private static async Task<IResult> ResolveAsync(
        string? transportModality,
        string? shipmentMode,
        string? direction,
        Guid? incotermId,
        string? serviceCodes,
        string? routeText,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        await using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.text, i.category, b.sort_order, i.sort_order
            FROM pricing."RateTermBlocks" b
            INNER JOIN pricing."RateTermBlockItems" i ON i.block_id = b.id
            INNER JOIN pricing."RateTermItems" t ON t.id = i.rate_term_item_id
            WHERE b.is_active = TRUE
              AND t.is_active = TRUE
              AND (@transport_modality IS NULL OR b.transport_modality IS NULL OR lower(b.transport_modality) = lower(@transport_modality))
              AND (@shipment_mode IS NULL OR b.shipment_mode IS NULL OR lower(b.shipment_mode) = lower(@shipment_mode))
              AND (@direction IS NULL OR b.direction IS NULL OR lower(b.direction) = lower(@direction))
              AND (@incoterm_id IS NULL OR b.incoterm_id IS NULL OR b.incoterm_id = @incoterm_id)
              AND (
                    b.route_key IS NULL
                    OR (
                        @route_text IS NOT NULL
                        AND position(lower(b.route_key) in lower(@route_text)) > 0
                    )
                  )
              AND (
                    NOT EXISTS (
                        SELECT 1 FROM pricing."RateTermBlockServices" bs0 WHERE bs0.block_id = b.id
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM pricing."RateTermBlockServices" bs
                        WHERE bs.block_id = b.id
                          AND lower(bs.service_code) = ANY(string_to_array(lower(COALESCE(@service_codes, '')), ','))
                    )
                  )
            ORDER BY b.sort_order, i.sort_order, t.text;
            """;

        Add(command, "transport_modality", Normalize(transportModality), DbType.String);
        Add(command, "shipment_mode", Normalize(shipmentMode), DbType.String);
        Add(command, "direction", Normalize(direction), DbType.String);
        Add(command, "incoterm_id", incotermId, DbType.Guid);
        Add(command, "service_codes", NormalizeServiceCodes(serviceCodes), DbType.String);
        Add(command, "route_text", NormalizeRouteText(routeText), DbType.String);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var resolved = new Dictionary<Guid, RankedTerm>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var text = reader.GetString(1);
            var category = NormalizeCategory(reader.GetString(2));
            var rank = CategoryRank(category);
            var sortOrder = reader.GetInt32(3) * 1000 + reader.GetInt32(4);

            if (!resolved.TryGetValue(id, out var existing) || rank > existing.Rank)
                resolved[id] = new RankedTerm(new CommercialTermItemDto(id, text), category, rank, sortOrder);
        }

        var values = resolved.Values.OrderBy(x => x.SortOrder).ThenBy(x => x.Item.Text).ToArray();
        return Results.Ok(
            new CommercialTermsDto(
                values.Where(x => x.Category == "Includes").Select(x => x.Item).ToArray(),
                values.Where(x => x.Category == "SubjectTo").Select(x => x.Item).ToArray(),
                values.Where(x => x.Category == "Excludes").Select(x => x.Item).ToArray()
            )
        );
    }

    private static string NormalizeCategory(string value) =>
        value.Equals("Includes", StringComparison.OrdinalIgnoreCase)
            ? "Includes"
            : value.Equals("SubjectTo", StringComparison.OrdinalIgnoreCase)
                ? "SubjectTo"
                : "Excludes";

    private static int CategoryRank(string category) => category switch
    {
        "Includes" => 3,
        "SubjectTo" => 2,
        _ => 1,
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeRouteText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(character =>
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark
            )
            .Aggregate(new System.Text.StringBuilder(), (builder, character) =>
            {
                builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
                return builder;
            })
            .ToString();
    }

    private static string? NormalizeServiceCodes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var codes = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return codes.Length == 0 ? null : string.Join(',', codes);
    }

    private static void Add(DbCommand command, string name, object? value, DbType dbType)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.DbType = dbType;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record RankedTerm(
        CommercialTermItemDto Item,
        string Category,
        int Rank,
        int SortOrder
    );

    public sealed record CommercialTermItemDto(Guid Id, string Text);
    public sealed record CommercialTermsDto(
        IReadOnlyCollection<CommercialTermItemDto> Includes,
        IReadOnlyCollection<CommercialTermItemDto> SubjectTo,
        IReadOnlyCollection<CommercialTermItemDto> Excludes
    );
}

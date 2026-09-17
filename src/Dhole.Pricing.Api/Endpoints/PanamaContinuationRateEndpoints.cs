using System.Data.Common;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Imports.GetPanamaContinuationRates;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class PanamaContinuationRateEndpoints
{
    public static IEndpointRouteBuilder MapPanamaContinuationRateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/import-rates/panama-continuation", GetPanamaContinuationRatesAsync)
            .WithTags("Imported FCL Rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);

        app.MapGet("/api/pricing/panama-continuation/land", GetPanamaLandContinuationAsync)
            .WithTags("FTL tariffs")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.CostSelect);

        return app;
    }

    private static async Task<IResult> GetPanamaContinuationRatesAsync(
        string panamaPol,
        string finalDestination,
        string? containerType,
        DateTime? quoteDate,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(panamaPol) || string.IsNullOrWhiteSpace(finalDestination))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidPanamaContinuationRoute",
                "El POL de Panamá y el destino final son obligatorios para buscar el segundo tramo marítimo.",
                httpContext);
        }

        var result = await dispatcher.DispatchAsync(
            new GetPanamaContinuationRatesQuery(
                panamaPol,
                finalDestination,
                containerType,
                quoteDate?.Date),
            cancellationToken);

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> GetPanamaLandContinuationAsync(
        string panamaPol,
        string finalDestination,
        string? containerType,
        string? panamaPolCode,
        string? finalDestinationCode,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(panamaPol) || string.IsNullOrWhiteSpace(finalDestination))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidPanamaContinuationRoute",
                "El POE de Panamá y el destino final son obligatorios para buscar el tramo marítimo-terrestre.",
                httpContext);
        }

        await using var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                origin_id,
                origin_name,
                origin_code,
                destination_id,
                destination_name,
                destination_code,
                equipment_class,
                equipment_label,
                currency_id,
                currency_name,
                currency_code,
                price_amount,
                transit_days,
                source,
                notes,
                is_active
            FROM pricing."FtlTariffs"
            WHERE is_active = TRUE
              AND upper(equipment_class) = upper(@equipment_class)
              AND
              (
                  (
                      @origin_code <> ''
                      AND lower(trim(COALESCE(origin_code, ''))) = lower(trim(@origin_code))
                  )
                  OR lower(trim(origin_name)) = lower(trim(@origin_name))
                  OR lower(origin_name) LIKE '%' || lower(trim(split_part(@origin_name, ',', 1))) || '%'
                  OR lower(@origin_name) LIKE '%' || lower(trim(origin_name)) || '%'
              )
              AND
              (
                  (
                      @destination_code <> ''
                      AND lower(trim(COALESCE(destination_code, ''))) = lower(trim(@destination_code))
                  )
                  OR lower(trim(destination_name)) = lower(trim(@destination_name))
                  OR lower(destination_name) LIKE '%' || lower(trim(split_part(@destination_name, ',', 1))) || '%'
                  OR lower(@destination_name) LIKE '%' || lower(trim(destination_name)) || '%'
              )
            ORDER BY
                CASE
                    WHEN @origin_code <> '' AND lower(trim(COALESCE(origin_code, ''))) = lower(trim(@origin_code)) THEN 0
                    WHEN lower(trim(origin_name)) = lower(trim(@origin_name)) THEN 1
                    ELSE 2
                END,
                CASE
                    WHEN @destination_code <> '' AND lower(trim(COALESCE(destination_code, ''))) = lower(trim(@destination_code)) THEN 0
                    WHEN lower(trim(destination_name)) = lower(trim(@destination_name)) THEN 1
                    ELSE 2
                END,
                COALESCE(updated_at_utc, created_at_utc) DESC
            LIMIT 1;
            """;

        Add(command, "equipment_class", ResolveLandEquipmentClass(containerType));
        Add(command, "origin_name", panamaPol.Trim());
        Add(command, "origin_code", panamaPolCode?.Trim() ?? string.Empty);
        Add(command, "destination_name", finalDestination.Trim());
        Add(command, "destination_code", finalDestinationCode?.Trim() ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Results.Ok((FtlTariffDto?)null);
        }

        return Results.Ok(ReadFtlTariff(reader));
    }

    private static string ResolveLandEquipmentClass(string? containerType)
    {
        var normalized = (containerType ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Contains("5 TON", StringComparison.Ordinal)
            || normalized.Contains("6 TON", StringComparison.Ordinal)
            || normalized.Contains("7 TON", StringComparison.Ordinal)
            || normalized.Contains("5-7", StringComparison.Ordinal))
        {
            return "5_7_TON";
        }

        // A continuación FCL desde Panamá viaja como unidad completa sobre chasis/camión.
        // La matriz terrestre existente representa esas unidades completas como 48/53.
        return "48_53";
    }

    private static FtlTariffDto ReadFtlTariff(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetGuid(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetDecimal(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.GetBoolean(16)
        );

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

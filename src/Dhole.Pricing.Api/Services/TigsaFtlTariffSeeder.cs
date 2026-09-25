using System.Data.Common;
using System.Globalization;
using System.Text;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Ensures the official GCF LTL matrices exist.
/// FTL tariffs are intentionally NOT seeded: they are maintained manually.
/// </summary>
public static class TigsaFtlTariffSeeder
{
    // Cliente final and NVOCC are separate commercial matrices for the same routes.
    private static readonly LtlSeedRow[] LtlTariffs =
    [
        new("San José, Costa Rica", "Managua, Nicaragua", 40m, 55m, 3, "2 - 3 días", "Almacén Fiscal Premier 6117", "FinalClient", "GCF Centroamérica LTL Pricing Engine v2.4"),
        new("San José, Costa Rica", "San Pedro Sula, Honduras", 50m, 55m, 6, "4 - 6 días", "Sicarga", "FinalClient", "GCF Centroamérica LTL Pricing Engine v2.4"),
        new("San José, Costa Rica", "San Salvador, El Salvador", 40m, 55m, 5, "4 - 5 días", "Central Logistics SA De C.V.", "FinalClient", "GCF Centroamérica LTL Pricing Engine v2.4"),
        new("San José, Costa Rica", "Ciudad Guatemala, Guatemala", 48m, 75m, 7, "5 - 7 días", "Almacenadora Integrada", "FinalClient", "GCF Centroamérica LTL Pricing Engine v2.4"),
        new("CFZ Panamá", "Managua, Nicaragua", 50m, 65m, 4, "3 - 4 días", "Almacén Fiscal Premier 6117", "FinalClient", "GCF Centroamérica LTL Pricing Engine v2.4"),
        new("CFZ Panamá", "San Pedro Sula, Honduras", 60m, 65m, 5, "4 - 5 días", "Sicarga", "FinalClient", "GCF Centroamérica LTL Pricing Engine v2.4"),
        new("CFZ Panamá", "San Salvador, El Salvador", 50m, 60m, 5, "4 - 5 días", "Central Logistics SA De C.V.", "FinalClient", "GCF Centroamérica LTL Pricing Engine v2.4"),
        new("CFZ Panamá", "Ciudad Guatemala, Guatemala", 58m, 80m, 6, "5 - 6 días", "Almacenadora Integrada", "FinalClient", "GCF Centroamérica LTL Pricing Engine v2.4"),

        new("San José, Costa Rica", "Managua, Nicaragua", 35m, 50m, 3, "2 - 3 días", "Almacén Fiscal Premier 6117", "Nvocc", "LTL GFC NVOCC CENTROAMERICA v1.3"),
        new("San José, Costa Rica", "San Pedro Sula, Honduras", 45m, 50m, 6, "4 - 6 días", "Sicarga", "Nvocc", "LTL GFC NVOCC CENTROAMERICA v1.3"),
        new("San José, Costa Rica", "San Salvador, El Salvador", 35m, 50m, 5, "4 - 5 días", "Central Logistics SA De C.V.", "Nvocc", "LTL GFC NVOCC CENTROAMERICA v1.3"),
        new("San José, Costa Rica", "Ciudad Guatemala, Guatemala", 45m, 50m, 7, "5 - 7 días", "Almacenadora Integrada", "Nvocc", "LTL GFC NVOCC CENTROAMERICA v1.3"),
        new("CFZ Panamá", "Managua, Nicaragua", 45m, 60m, 4, "3 - 4 días", "Almacén Fiscal Premier 6117", "Nvocc", "LTL GFC NVOCC CENTROAMERICA v1.3"),
        new("CFZ Panamá", "San Pedro Sula, Honduras", 55m, 60m, 5, "4 - 5 días", "Sicarga", "Nvocc", "LTL GFC NVOCC CENTROAMERICA v1.3"),
        new("CFZ Panamá", "San Salvador, El Salvador", 45m, 60m, 5, "4 - 5 días", "Central Logistics SA De C.V.", "Nvocc", "LTL GFC NVOCC CENTROAMERICA v1.3"),
        new("CFZ Panamá", "Ciudad Guatemala, Guatemala", 55m, 60m, 6, "5 - 6 días", "Almacenadora Integrada", "Nvocc", "LTL GFC NVOCC CENTROAMERICA v1.3"),
    ];

    public static async Task SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("LandLtlTariffSeeder");
        var configCatalog = services.GetRequiredService<IPricingConfigCatalogClient>();
        var db = services.GetRequiredService<ServiceDbContext>();

        try
        {
            var origins = await LoadFirstCatalogAsync(
                configCatalog,
                ["land-pol", PricingConstants.CatalogSlugs.Pol],
                cancellationToken
            );
            var destinations = await LoadFirstCatalogAsync(
                configCatalog,
                ["land-poe", PricingConstants.CatalogSlugs.Poe],
                cancellationToken
            );
            var currencies = await configCatalog.GetActiveByGroupAsync(
                PricingConstants.CatalogSlugs.Currencies,
                cancellationToken
            );

            var usd = currencies
                .OrderByDescending(item => IsExact(item.Code, "USD"))
                .ThenByDescending(item => IsExact(item.Value, "USD"))
                .FirstOrDefault(item =>
                    IsExact(item.Code, "USD")
                    || IsExact(item.Value, "USD")
                    || Normalize(item.Name).Contains("dolar")
                    || Normalize(item.Name).Contains("dollar")
                );

            if (usd is null)
            {
                logger.LogWarning("No se cargó la matriz LTL porque no existe USD activo en Config.");
                return;
            }

            await using var connection = db.Database.GetDbConnection();
            await EnsureOpenAsync(connection, cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var created = await SeedLtlTariffsAsync(
                connection,
                transaction,
                origins,
                destinations,
                usd,
                cancellationToken
            );

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                created > 0
                    ? "Se agregaron {Count} filas faltantes a la matriz LTL GCF."
                    : "La matriz LTL GCF ya estaba completa; no se sobrescribieron cambios manuales.",
                created
            );
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "No fue posible completar la matriz LTL. Se reintentará en el próximo arranque."
            );
        }
    }

    private static async Task<int> SeedLtlTariffsAsync(
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyCollection<PricingConfigCatalogItem> origins,
        IReadOnlyCollection<PricingConfigCatalogItem> destinations,
        PricingConfigCatalogItem usd,
        CancellationToken cancellationToken
    )
    {
        var created = 0;

        foreach (var row in LtlTariffs)
        {
            var origin = ResolveFreeformRouteItem(origins, row.Origin);
            var destination = ResolveFreeformRouteItem(destinations, row.Destination);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO pricing."FtlTariffs"
                (
                    id,
                    origin_id,
                    origin_name,
                    origin_code,
                    destination_id,
                    destination_name,
                    destination_code,
                    shipment_mode,
                    commercial_profile,
                    equipment_class,
                    equipment_label,
                    applicable_equipment_classes,
                    currency_id,
                    currency_name,
                    currency_code,
                    price_amount,
                    rate_basis,
                    minimum_amount,
                    transit_days,
                    warehouse_name,
                    source,
                    notes,
                    valid_from,
                    valid_to,
                    is_active,
                    created_at_utc
                )
                SELECT
                    @id,
                    @origin_id,
                    @origin_name,
                    @origin_code,
                    @destination_id,
                    @destination_name,
                    @destination_code,
                    'Ltl',
                    @commercial_profile,
                    'LTL_CBM',
                    'LTL · USD/CBM',
                    '["LTL_CBM"]',
                    @currency_id,
                    @currency_name,
                    @currency_code,
                    @price_amount,
                    'PerCbm',
                    @minimum_amount,
                    @transit_days,
                    @warehouse_name,
                    @source,
                    @notes,
                    NULL,
                    NULL,
                    TRUE,
                    now()
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM pricing."FtlTariffs" existing
                    WHERE lower(existing.shipment_mode) = 'ltl'
                      AND lower(existing.commercial_profile) = lower(@commercial_profile)
                      AND upper(existing.equipment_class) = 'LTL_CBM'
                      AND lower(translate(trim(existing.origin_name), 'áéíóúüñ', 'aeiouun'))
                          = lower(translate(trim(@origin_name), 'áéíóúüñ', 'aeiouun'))
                      AND lower(translate(trim(existing.destination_name), 'áéíóúüñ', 'aeiouun'))
                          = lower(translate(trim(@destination_name), 'áéíóúüñ', 'aeiouun'))
                );
                """;

            Add(command, "id", Guid.NewGuid());
            Add(command, "origin_id", origin?.Id);
            Add(command, "origin_name", origin is null ? row.Origin : SnapshotName(origin));
            Add(command, "origin_code", origin?.Code);
            Add(command, "destination_id", destination?.Id);
            Add(command, "destination_name", destination is null ? row.Destination : SnapshotName(destination));
            Add(command, "destination_code", destination?.Code);
            Add(command, "commercial_profile", row.CommercialProfile);
            Add(command, "currency_id", usd.Id);
            Add(command, "currency_name", SnapshotName(usd));
            Add(command, "currency_code", CurrencyBusinessValue(usd));
            Add(command, "price_amount", row.PricePerCbm);
            Add(command, "minimum_amount", row.MinimumAmount);
            Add(command, "transit_days", row.TransitDays);
            Add(command, "warehouse_name", row.Warehouse);
            Add(command, "source", row.Source);
            Add(
                command,
                "notes",
                $"Servicio LTL consolidado terrestre · perfil {row.CommercialProfile}. " +
                $"Tránsito estimado original: {row.TransitRange}. " +
                "Tarifa USD/CBM con mínimo por ruta. Relación operativa de peso volumétrico: 1 CBM = 333.33 kg."
            );

            created += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return created;
    }

    private static PricingConfigCatalogItem? ResolveFreeformRouteItem(
        IReadOnlyCollection<PricingConfigCatalogItem> items,
        string location
    )
    {
        var needle = Normalize(location);
        var city = Normalize(location.Split(',', StringSplitOptions.TrimEntries)[0]);

        return items
            .Select(item =>
            {
                var fields = new[] { item.Name, item.Value ?? string.Empty, item.Code, item.Slug }
                    .Select(Normalize)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                var score = fields.Any(field => field == needle) ? 100
                    : fields.Any(field => needle.Contains(field, StringComparison.Ordinal) || field.Contains(needle, StringComparison.Ordinal)) ? 80
                    : fields.Any(field => city.Length >= 4 && (field.Contains(city, StringComparison.Ordinal) || city.Contains(field, StringComparison.Ordinal))) ? 60
                    : 0;
                return new { Item = item, Score = score };
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Name)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    private static async Task<IReadOnlyCollection<PricingConfigCatalogItem>> LoadFirstCatalogAsync(
        IPricingConfigCatalogClient client,
        IReadOnlyCollection<string> slugs,
        CancellationToken cancellationToken
    )
    {
        foreach (var slug in slugs)
        {
            try
            {
                var items = await client.GetActiveByGroupAsync(slug, cancellationToken);
                if (items.Count > 0) return items;
            }
            catch
            {
                // Try the compatibility catalog slug below.
            }
        }

        return Array.Empty<PricingConfigCatalogItem>();
    }

    private static string SnapshotName(PricingConfigCatalogItem item) =>
        string.IsNullOrWhiteSpace(item.Value) ? item.Name.Trim() : item.Value.Trim();

    private static string CurrencyBusinessValue(PricingConfigCatalogItem item)
    {
        foreach (var candidate in new[] { item.Value, item.Name, item.Code })
        {
            var value = candidate?.Trim();
            if (!string.IsNullOrWhiteSpace(value)
                && value.Length == 3
                && value.All(char.IsLetter))
            {
                return value.ToUpperInvariant();
            }
        }

        return SnapshotName(item);
    }

    private static bool IsExact(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('–', '-')
            .Replace('—', '-');
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record LtlSeedRow(
        string Origin,
        string Destination,
        decimal PricePerCbm,
        decimal MinimumAmount,
        int TransitDays,
        string TransitRange,
        string Warehouse,
        string CommercialProfile,
        string Source
    );
}

using System.Data.Common;
using System.Globalization;
using System.Text;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

public static class TigsaFtlTariffSeeder
{
    private const string LargeEquipmentClass = "48_53";
    private const string SmallEquipmentClass = "5_7_TON";

    private static readonly string[] Locations =
    [
        "Costa Rica",
        "Nicaragua",
        "Tegucigalpa",
        "San Pedro Sula",
        "El Salvador",
        "Guatemala",
        "Panamá",
        "Ciudad Hidalgo",
    ];

    private static readonly decimal?[,] LargeEquipmentPrices =
    {
        { null, 1400m, 1700m, 1900m, 1600m, 1700m, 1700m, 2700m },
        { 1000m, null, 900m, 1200m, 900m, 1200m, 2300m, 1700m },
        { 1500m, 1000m, null, 900m, 900m, 1300m, 2500m, 1700m },
        { 1900m, 1200m, 800m, null, 1100m, 1400m, 3400m, 2200m },
        { 1500m, 1000m, 900m, 1200m, null, 1200m, 2800m, 2000m },
        { 2400m, 1700m, 1400m, 1600m, 1200m, null, 3400m, 1300m },
        { 1700m, 2400m, 2500m, 3100m, 2600m, 3100m, null, 3900m },
        { 2700m, 2300m, 2100m, 2300m, 2000m, 1200m, 3900m, null },
    };

    private static readonly decimal?[,] SmallEquipmentPrices =
    {
        { null, 1100m, 1400m, 1650m, 1350m, 1550m, 1500m, 2000m },
        { 900m, null, 900m, 1200m, 900m, 1200m, 1900m, 1600m },
        { 1300m, 900m, null, 900m, 800m, 1200m, 2900m, 1600m },
        { 1600m, 1200m, 800m, null, 900m, 1200m, 3000m, 1700m },
        { 1200m, 900m, 800m, 900m, null, 800m, 2300m, 1200m },
        { 1500m, 1200m, 1100m, 1300m, 800m, null, 2500m, 900m },
        { 1100m, 1600m, 1800m, 2300m, 1900m, 2300m, null, 2900m },
        { 1900m, 1600m, 1600m, 1700m, 1300m, 900m, 2900m, null },
    };

    private static readonly IReadOnlyDictionary<string, string[]> LocationAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Costa Rica"] = ["costa rica"],
            ["Nicaragua"] = ["nicaragua"],
            ["Tegucigalpa"] = ["tegucigalpa"],
            ["San Pedro Sula"] = ["san pedro sula"],
            ["El Salvador"] = ["el salvador", "salvador"],
            ["Guatemala"] = ["guatemala"],
            ["Panamá"] = ["panama"],
            ["Ciudad Hidalgo"] = ["ciudad hidalgo"],
        };

    public static async Task SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TigsaFtlTariffSeeder");
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
                logger.LogWarning("No se cargó la matriz FTL porque no existe USD activo en Config.");
                return;
            }

            var routeCache = Locations.ToDictionary(
                location => location,
                location => (
                    Origin: ResolveRouteItem(origins, location),
                    Destination: ResolveRouteItem(destinations, location)
                ),
                StringComparer.OrdinalIgnoreCase
            );

            var unresolved = routeCache
                .Where(pair => pair.Value.Origin is null || pair.Value.Destination is null)
                .Select(pair => pair.Key)
                .ToArray();

            if (unresolved.Length > 0)
            {
                logger.LogWarning(
                    "No se pudieron resolver todas las ubicaciones de la matriz FTL en Config: {Locations}",
                    string.Join(", ", unresolved)
                );
            }

            await using var connection = db.Database.GetDbConnection();
            await EnsureOpenAsync(connection, cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var created = 0;
            created += await SeedMatrixAsync(
                connection,
                transaction,
                routeCache,
                usd,
                LargeEquipmentClass,
                "Equipo 48/53 pies",
                LargeEquipmentPrices,
                TransitDaysLarge,
                cancellationToken
            );
            created += await SeedMatrixAsync(
                connection,
                transaction,
                routeCache,
                usd,
                SmallEquipmentClass,
                "Equipo 5 a 7 toneladas",
                SmallEquipmentPrices,
                TransitDaysSmall,
                cancellationToken
            );

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                created > 0
                    ? "Se agregaron {Count} tarifas a la matriz maestra FTL."
                    : "La matriz maestra FTL ya estaba cargada; no se sobrescribieron cambios manuales.",
                created
            );
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "No fue posible completar la carga inicial de la matriz FTL. Se reintentará en el próximo arranque."
            );
        }
    }

    private static async Task<int> SeedMatrixAsync(
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyDictionary<string, (PricingConfigCatalogItem? Origin, PricingConfigCatalogItem? Destination)> routeCache,
        PricingConfigCatalogItem usd,
        string equipmentClass,
        string equipmentLabel,
        decimal?[,] prices,
        Func<string, string, int?> transitDays,
        CancellationToken cancellationToken
    )
    {
        var created = 0;

        for (var originIndex = 0; originIndex < Locations.Length; originIndex++)
        {
            for (var destinationIndex = 0; destinationIndex < Locations.Length; destinationIndex++)
            {
                var price = prices[originIndex, destinationIndex];
                if (!price.HasValue) continue;

                var originKey = Locations[originIndex];
                var destinationKey = Locations[destinationIndex];
                var origin = routeCache[originKey].Origin;
                var destination = routeCache[destinationKey].Destination;
                if (origin is null || destination is null) continue;

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
                        equipment_class,
                        equipment_label,
                        currency_id,
                        currency_name,
                        currency_code,
                        price_amount,
                        transit_days,
                        source,
                        notes,
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
                        @equipment_class,
                        @equipment_label,
                        @currency_id,
                        @currency_name,
                        @currency_code,
                        @price_amount,
                        @transit_days,
                        @source,
                        @notes,
                        TRUE,
                        now()
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM pricing."FtlTariffs" existing
                        WHERE upper(existing.equipment_class) = upper(@equipment_class)
                          AND lower(trim(existing.origin_name)) = lower(trim(@origin_name))
                          AND lower(trim(existing.destination_name)) = lower(trim(@destination_name))
                    );
                    """;

                Add(command, "id", Guid.NewGuid());
                Add(command, "origin_id", origin.Id);
                Add(command, "origin_name", SnapshotName(origin));
                Add(command, "origin_code", origin.Code);
                Add(command, "destination_id", destination.Id);
                Add(command, "destination_name", SnapshotName(destination));
                Add(command, "destination_code", destination.Code);
                Add(command, "equipment_class", equipmentClass);
                Add(command, "equipment_label", equipmentLabel);
                Add(command, "currency_id", usd.Id);
                Add(command, "currency_name", SnapshotName(usd));
                Add(command, "currency_code", usd.Code);
                Add(command, "price_amount", price.Value);
                Add(command, "transit_days", transitDays(originKey, destinationKey));
                Add(command, "source", "TIGSA · oferta 08-05-2026");
                Add(
                    command,
                    "notes",
                    "Matriz COMPLETOS. Precio base USD por camión. La oferta original indica vigencia desde 11-05-2026 por 60 días naturales."
                );

                created += await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        return created;
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

    private static PricingConfigCatalogItem? ResolveRouteItem(
        IReadOnlyCollection<PricingConfigCatalogItem> items,
        string location
    )
    {
        var aliases = LocationAliases[location];
        return items
            .Select(item => new { Item = item, Score = RouteScore(item, aliases) })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Name)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    private static int RouteScore(PricingConfigCatalogItem item, IReadOnlyCollection<string> aliases)
    {
        var fields = new[] { item.Name, item.Value ?? string.Empty, item.Code, item.Slug }
            .Select(Normalize)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        var score = 0;
        foreach (var aliasRaw in aliases)
        {
            var alias = Normalize(aliasRaw);
            if (fields.Any(field => field == alias)) score = Math.Max(score, 100);
            else if (fields.Any(field => field.Contains(alias, StringComparison.Ordinal))) score = Math.Max(score, 65);
        }

        return score;
    }

    private static int? TransitDaysLarge(string origin, string destination) =>
        (origin, destination) switch
        {
            ("Costa Rica", "Nicaragua") => 4,
            ("Costa Rica", "Tegucigalpa") => 6,
            ("Costa Rica", "El Salvador") => 6,
            ("Costa Rica", "Panamá") => 5,
            ("Costa Rica", "San Pedro Sula") => 7,
            ("Costa Rica", "Guatemala") => 7,
            ("Ciudad Hidalgo", "Costa Rica") => 8,
            ("Ciudad Hidalgo", "Panamá") => 9,
            ("Guatemala", "Costa Rica") => 7,
            _ => null,
        };

    private static int? TransitDaysSmall(string origin, string destination) =>
        (origin, destination) switch
        {
            ("Costa Rica", "Nicaragua") => 4,
            ("Costa Rica", "Tegucigalpa") => 5,
            ("Costa Rica", "El Salvador") => 5,
            ("Costa Rica", "Panamá") => 5,
            ("Costa Rica", "San Pedro Sula") => 6,
            ("Costa Rica", "Guatemala") => 6,
            ("Ciudad Hidalgo", "Costa Rica") => 7,
            ("Ciudad Hidalgo", "Panamá") => 8,
            ("Guatemala", "Costa Rica") => 6,
            _ => null,
        };

    private static string SnapshotName(PricingConfigCatalogItem item) =>
        string.IsNullOrWhiteSpace(item.Value) ? item.Name.Trim() : item.Value.Trim();

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
}

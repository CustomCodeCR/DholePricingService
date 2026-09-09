using System.Globalization;
using System.Text;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Api.Services;

public static class TigsaFtlCostSeeder
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
        { 1600m, 1200m, 800m, null, 900m, 1200m, 3300m, 1700m },
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
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TigsaFtlCostSeeder");
        var costs = services.GetRequiredService<ICostRepository>();
        var configCatalog = services.GetRequiredService<IPricingConfigCatalogClient>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        try
        {
            var polItems = await configCatalog.GetActiveByGroupAsync(
                PricingConstants.CatalogSlugs.Pol,
                cancellationToken
            );
            var poeItems = await configCatalog.GetActiveByGroupAsync(
                PricingConstants.CatalogSlugs.Poe,
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
                logger.LogWarning("No se cargaron tarifas TIGSA FTL porque no existe USD activo en Config.");
                return;
            }

            var routeCache = new Dictionary<string, (PricingConfigCatalogItem? Pol, PricingConfigCatalogItem? Poe)>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var location in Locations)
            {
                routeCache[location] = (
                    ResolveRouteItem(polItems, location),
                    ResolveRouteItem(poeItems, location)
                );
            }

            var unresolved = routeCache
                .Where(pair => pair.Value.Pol is null || pair.Value.Poe is null)
                .Select(pair =>
                    $"{pair.Key} (POL={(pair.Value.Pol is null ? "faltante" : "ok")}, POE={(pair.Value.Poe is null ? "faltante" : "ok")})"
                )
                .ToArray();

            if (unresolved.Length > 0)
            {
                logger.LogWarning(
                    "Algunas ubicaciones TIGSA FTL no existen todavía como rutas SD en Config: {Locations}",
                    string.Join(", ", unresolved)
                );
            }

            var created = 0;
            created += await SeedMatrixAsync(
                costs,
                routeCache,
                usd,
                LargeEquipmentClass,
                "Equipo 48/53 pies",
                LargeEquipmentPrices,
                TransitDaysLarge,
                cancellationToken
            );
            created += await SeedMatrixAsync(
                costs,
                routeCache,
                usd,
                SmallEquipmentClass,
                "Equipo 5 a 7 toneladas",
                SmallEquipmentPrices,
                TransitDaysSmall,
                cancellationToken
            );

            if (created == 0)
            {
                logger.LogInformation("La matriz TIGSA FTL ya estaba cargada; no se sobrescribieron cambios manuales.");
                return;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Se cargaron {Count} tarifas TIGSA FTL en la matriz editable de Costos.",
                created
            );
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "No fue posible completar la carga inicial de tarifas TIGSA FTL. Se reintentará en el próximo arranque."
            );
        }
    }

    private static async Task<int> SeedMatrixAsync(
        ICostRepository costs,
        IReadOnlyDictionary<string, (PricingConfigCatalogItem? Pol, PricingConfigCatalogItem? Poe)> routeCache,
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
                var origin = routeCache[originKey].Pol;
                var destination = routeCache[destinationKey].Poe;
                if (origin is null || destination is null) continue;

                var name = $"FTL TIGSA · {originKey} → {destinationKey} · {equipmentLabel}";
                var exists = await costs.ExistsByNameAsync(
                    name,
                    CostType.Fixed,
                    CostDetailType.Freight,
                    portId: null,
                    portRole: null,
                    polId: origin.Id,
                    poeId: destination.Id,
                    podId: null,
                    carrierId: null,
                    agentId: null,
                    shipmentMode: ShipmentMode.Ftl,
                    chargeBasis: ChargeBasis.PerTruck,
                    excludeId: null,
                    cancellationToken: cancellationToken
                );

                if (exists) continue;

                var days = transitDays(originKey, destinationKey);
                var transitTag = days.HasValue ? $" [TRANSIT_DAYS={days.Value}]" : string.Empty;
                var notes =
                    $"[FTL_EQUIPMENT_CLASS={equipmentClass}]{transitTag} "
                    + "Fuente: TIGSA, oferta 08-05-2026. "
                    + "Matriz COMPLETOS; precio base USD por camión. "
                    + "La oferta original indica vigencia desde 11-05-2026 por 60 días. "
                    + "Registro cargado como matriz maestra editable en Pricing.";

                var cost = Cost.Create(
                    name,
                    CostType.Fixed,
                    CostDetailType.Freight,
                    carrierId: null,
                    carrierName: null,
                    carrierCode: null,
                    agentId: null,
                    agentName: null,
                    agentCode: null,
                    portId: null,
                    portName: null,
                    portCode: null,
                    portRole: null,
                    polId: origin.Id,
                    polName: SnapshotName(origin),
                    polCode: origin.Code,
                    poeId: destination.Id,
                    poeName: SnapshotName(destination),
                    poeCode: destination.Code,
                    podId: null,
                    podName: null,
                    podCode: null,
                    incoterms: Array.Empty<CostIncotermSelection>(),
                    currencyId: usd.Id,
                    currencyName: SnapshotName(usd),
                    currencyCode: usd.Code,
                    costAmount: price.Value,
                    saleAmount: price.Value,
                    notes: notes,
                    isAccountant: true,
                    shipmentMode: ShipmentMode.Ftl,
                    chargeBasis: ChargeBasis.PerTruck,
                    minimumCostAmount: null,
                    minimumSaleAmount: null,
                    kgPerCbm: null,
                    createdBy: null
                );

                await costs.AddAsync(cost, cancellationToken);
                created++;
            }
        }

        return created;
    }

    private static PricingConfigCatalogItem? ResolveRouteItem(
        IReadOnlyCollection<PricingConfigCatalogItem> items,
        string location
    )
    {
        var aliases = LocationAliases[location];
        return items
            .Select(item => new
            {
                Item = item,
                Score = RouteScore(item, aliases),
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Name)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    private static int RouteScore(PricingConfigCatalogItem item, IReadOnlyCollection<string> aliases)
    {
        var fields = new[]
        {
            item.Name,
            item.Value ?? string.Empty,
            item.Code,
            item.Slug,
        }
        .Select(Normalize)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();

        var score = 0;
        foreach (var aliasRaw in aliases)
        {
            var alias = Normalize(aliasRaw);
            if (fields.Any(field => field == alias))
                score = Math.Max(score, 100);
            else if (fields.Any(field => field.Contains(alias, StringComparison.Ordinal)))
                score = Math.Max(score, 65);
        }

        var metadata = Normalize(item.MetadataJson ?? string.Empty);
        var code = Normalize(item.Code);
        if (
            metadata.Contains("\"terminaltype\":\"sd\"", StringComparison.Ordinal)
            || metadata.Contains("\"terminaltype\": \"sd\"", StringComparison.Ordinal)
            || code.StartsWith("sd-", StringComparison.Ordinal)
            || code.StartsWith("sd_", StringComparison.Ordinal)
            || code.EndsWith("-sd", StringComparison.Ordinal)
            || code.EndsWith("_sd", StringComparison.Ordinal)
        )
        {
            score += 25;
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
}

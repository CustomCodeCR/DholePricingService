using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Services;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Imports;

public sealed class AgentOceanFreightImportService(
    IImportFclRateRepository importRates,
    IPricingConfigCatalogClient catalogs,
    IImportedRateChangeNotificationService notifications,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<AgentOceanFreightImportResult> PersistAsync(
        Guid resultId,
        Guid executionId,
        string dataJson,
        DateTime extractedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (resultId == Guid.Empty || executionId == Guid.Empty)
            throw new InvalidOperationException("El resultado y la ejecución de Agent son requeridos.");

        if (string.IsNullOrWhiteSpace(dataJson))
            return new AgentOceanFreightImportResult(0, 0);

        var payload = JsonSerializer.Deserialize<AgentOceanFreightPayload>(dataJson, JsonOptions)
            ?? throw new InvalidOperationException("Agent no devolvió un resultado de tarifas válido.");

        var extractedAt = EnsureUtc(extractedAtUtc);
        var costaRicaTime = ToCostaRica(extractedAt);
        var spotDate = costaRicaTime.Date;

        var profile = await ResolvePreferredProfileAsync(cancellationToken);
        var carrier = await ResolveCatalogAsync(
            "carriers",
            "MAEU",
            payload.ProviderName ?? payload.Provider ?? "Maersk",
            "MAEU",
            "Maersk",
            cancellationToken);
        var pendingAgent = CreateFallbackSnapshot("agents", "Por asignar", "PENDING");
        var pendingPod = CreateFallbackSnapshot("pod", "Por asignar", "PENDING");

        var existingIds = (await importRates.GetByImportFclBatchIdAsync(
            executionId,
            cancellationToken))
            .Select(x => x.ExtractionRecordId)
            .ToHashSet();

        var created = new List<ImportFclRates>();
        var skipped = 0;

        foreach (var result in payload.Data?.Results ?? [])
        {
            if (!string.Equals(result.Status, "Available", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var route = result.Route;
            var equipment = result.Equipment;
            if (route is null || equipment is null)
            {
                skipped++;
                continue;
            }

            var pol = await ResolveCatalogAsync(
                "pol",
                route.PolCode,
                route.PolName,
                route.PolCode ?? "PENDING",
                route.PolName ?? "Por asignar",
                cancellationToken);
            var poe = await ResolveCatalogAsync(
                "poe",
                route.PoeCode ?? route.PodCode,
                route.PoeName ?? route.PodName,
                route.PoeCode ?? route.PodCode ?? "PENDING",
                route.PoeName ?? route.PodName ?? "Por asignar",
                cancellationToken);
            var pod = !string.IsNullOrWhiteSpace(route.PodName) || !string.IsNullOrWhiteSpace(route.PodCode)
                ? await ResolveCatalogAsync(
                    "pod",
                    route.PodCode,
                    route.PodName,
                    route.PodCode ?? "PENDING",
                    route.PodName ?? "Por asignar",
                    cancellationToken)
                : pendingPod;

            var container = await ResolveCatalogAsync(
                "container-types",
                equipment.Name ?? equipment.Code,
                equipment.Name ?? equipment.Code,
                equipment.Name ?? equipment.Code ?? "PENDING",
                equipment.Name ?? equipment.Code ?? "Por asignar",
                cancellationToken);

            foreach (var offer in result.Offers ?? [])
            {
                if (!offer.Available || offer.OceanFreight is null)
                {
                    skipped++;
                    continue;
                }

                var extractionRecordId = DeterministicGuid(
                    $"{resultId:N}|{result.RouteId}|{result.EquipmentId}|{offer.ExternalRouteId}");

                if (existingIds.Contains(extractionRecordId))
                {
                    skipped++;
                    continue;
                }

                var currency = await ResolveCatalogAsync(
                    "currencies",
                    offer.OceanFreight.Currency,
                    offer.OceanFreight.Currency,
                    offer.OceanFreight.Currency,
                    offer.OceanFreight.Currency,
                    cancellationToken);

                var originCharges = SumCharges(offer.Charges, "Origin");
                var destinationCharges = SumCharges(offer.Charges, "Destination");
                var surcharges = (offer.Charges ?? [])
                    .Where(x =>
                        !EqualsText(x.Application, "Freight")
                        && !EqualsText(x.Application, "Origin")
                        && !EqualsText(x.Application, "Destination")
                        && !EqualsText(x.Code, "BAS"))
                    .Sum(x => x.Amount);

                var totalCost = offer.AllIn?.Amount
                    ?? (offer.Charges?.Count > 0
                        ? offer.Charges.Sum(x => x.Amount)
                        : offer.OceanFreight.Amount + originCharges + destinationCharges + surcharges);

                var validTo = ResolveOfferValidTo(spotDate, offer);

                var rawDataJson = JsonSerializer.Serialize(new
                {
                    _dholeSource = new
                    {
                        sourceType = "AgentExtraction",
                        provider = payload.Provider ?? "MAERSK",
                        providerName = payload.ProviderName ?? "Maersk",
                        resultId,
                        executionId,
                        extractedAtUtc = extractedAt,
                        extractedAtCostaRica = costaRicaTime,
                    },
                    route,
                    equipment,
                    offer,
                }, JsonOptions);

                var entity = ImportFclRates.Create(
                    executionId,
                    extractionRecordId,
                    ImportSourceType.AgentExtraction,
                    profile,
                    pol,
                    poe,
                    pod,
                    carrier,
                    pendingAgent,
                    container,
                    currency,
                    payload.Commodity,
                    null,
                    offer.OceanFreight.Amount,
                    originCharges,
                    destinationCharges,
                    surcharges,
                    totalCost,
                    null,
                    null,
                    null,
                    0,
                    Math.Max(0, offer.TransitDays),
                    spotDate,
                    validTo,
                    rawDataJson,
                    null);

                await importRates.AddAsync(entity, cancellationToken);
                await notifications.QueueVariationNotificationsAsync(entity, cancellationToken);

                created.Add(entity);
                existingIds.Add(extractionRecordId);
            }
        }

        if (created.Count == 0)
            return new AgentOceanFreightImportResult(0, skipped);

        await notifications.QueueApprovalRequiredNotificationsAsync(
            created[0],
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var rate in created)
        {
            await cache.RemoveImportRateCacheAsync(
                rate.Id,
                executionId,
                cancellationToken);
        }

        await cache.RemoveImportRatesAsync(
            importBatchId: executionId,
            sourceType: ImportSourceType.AgentExtraction,
            cancellationToken: cancellationToken);

        return new AgentOceanFreightImportResult(created.Count, skipped);
    }

    private async Task<CatalogSnapshot> ResolvePreferredProfileAsync(
        CancellationToken cancellationToken)
    {
        var items = await catalogs.GetActiveByGroupAsync(
            "pricing-imports-profiles",
            cancellationToken);

        var preferred = items.FirstOrDefault(x =>
            Normalize(x.Name).Contains("estandar", StringComparison.Ordinal)
            || Normalize(x.Value).Contains("estandar", StringComparison.Ordinal)
            || Normalize(x.Name).Contains("standard", StringComparison.Ordinal)
            || Normalize(x.Value).Contains("standard", StringComparison.Ordinal));

        return preferred is not null
            ? ToSnapshot(preferred)
            : CreateFallbackSnapshot(
                "pricing-imports-profiles",
                "Extracción Maersk",
                "AGENT-MAERSK");
    }

    private async Task<CatalogSnapshot> ResolveCatalogAsync(
        string group,
        string? code,
        string? name,
        string fallbackCode,
        string fallbackName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var byCode = await catalogs.GetActiveByCodeAsync(
                group,
                code.Trim(),
                cancellationToken);

            if (byCode is not null)
                return ToSnapshot(byCode);
        }

        var items = await catalogs.GetActiveByGroupAsync(group, cancellationToken);
        var wanted = new[] { code, name }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Normalize(x))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var exact = items.FirstOrDefault(item =>
        {
            var values = new[] { item.Code, item.Name, item.Value, item.Slug }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Normalize(x))
                .ToArray();

            return wanted.Any(target => values.Contains(target, StringComparer.Ordinal));
        });

        if (exact is not null)
            return ToSnapshot(exact);

        var fuzzy = items.FirstOrDefault(item =>
        {
            var values = new[] { item.Code, item.Name, item.Value, item.Slug }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Normalize(x))
                .Where(x => x.Length >= 3)
                .ToArray();

            return wanted.Any(target =>
                target.Length >= 3
                && values.Any(value =>
                    value.Contains(target, StringComparison.Ordinal)
                    || target.Contains(value, StringComparison.Ordinal)));
        });

        return fuzzy is not null
            ? ToSnapshot(fuzzy)
            : CreateFallbackSnapshot(group, fallbackName, fallbackCode);
    }

    private static DateTime ResolveOfferValidTo(DateTime spotDate, AgentOffer offer)
    {
        var validTo = offer.CargoCutoff?.Date
            ?? offer.Etd?.Date
            ?? spotDate.AddDays(7);

        return validTo < spotDate ? spotDate : validTo;
    }

    private static decimal SumCharges(
        IReadOnlyCollection<AgentCharge>? charges,
        string application)
        => (charges ?? [])
            .Where(x => EqualsText(x.Application, application))
            .Sum(x => x.Amount);

    private static bool EqualsText(string? left, string right)
        => string.Equals(left?.Trim(), right, StringComparison.OrdinalIgnoreCase);

    private static CatalogSnapshot ToSnapshot(PricingConfigCatalogItem item)
        => CatalogSnapshot.Create(
            item.Id,
            item.SnapshotName(preferValue: false),
            item.Code,
            item.Slug);

    private static CatalogSnapshot CreateFallbackSnapshot(
        string group,
        string name,
        string code)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name)
            ? "Por asignar"
            : name.Trim();
        var normalizedCode = string.IsNullOrWhiteSpace(code)
            ? "PENDING"
            : code.Trim().ToUpperInvariant();
        var slug = Normalize(normalizedName).Replace(' ', '-');
        if (string.IsNullOrWhiteSpace(slug))
            slug = "pending";

        var id = DeterministicGuid($"{group}|{slug}");
        return CatalogSnapshot.Create(id, normalizedName, normalizedCode, slug);
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return string.Join(
            ' ',
            builder.ToString().Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value == default)
            return DateTime.UtcNow;

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    private static DateTime ToCostaRica(DateTime utc)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");
            return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        }
        catch (TimeZoneNotFoundException)
        {
            return utc.AddHours(-6);
        }
        catch (InvalidTimeZoneException)
        {
            return utc.AddHours(-6);
        }
    }

    private sealed record AgentOceanFreightPayload(
        string? Provider,
        string? ProviderName,
        string? Commodity,
        AgentOceanFreightData? Data);

    private sealed record AgentOceanFreightData(
        IReadOnlyCollection<AgentSearchResult>? Results);

    private sealed record AgentSearchResult(
        string? RouteId,
        string? EquipmentId,
        string? Status,
        AgentRoute? Route,
        AgentEquipment? Equipment,
        IReadOnlyCollection<AgentOffer>? Offers);

    private sealed record AgentRoute(
        string? PolCode,
        string? PolName,
        string? PoeCode,
        string? PoeName,
        string? PodCode,
        string? PodName);

    private sealed record AgentEquipment(
        string? Code,
        string? Name,
        int Quantity,
        decimal DefaultWeightKg);

    private sealed record AgentOffer(
        string ExternalRouteId,
        bool Available,
        DateTimeOffset? Etd,
        DateTimeOffset? Eta,
        int TransitDays,
        string? Vessel,
        string? Voyage,
        AgentMoney? OceanFreight,
        AgentMoney? AllIn,
        IReadOnlyCollection<AgentCharge>? Charges,
        DateTimeOffset? CargoCutoff);

    private sealed record AgentMoney(
        string Currency,
        decimal Amount);

    private sealed record AgentCharge(
        string Code,
        string? Name,
        string Currency,
        decimal Amount,
        string? Application);
}

public sealed record AgentOceanFreightImportResult(
    int CreatedRows,
    int SkippedRows);

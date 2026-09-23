using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Features.Imports.GetImportRatesForSelect;

public sealed class GetImportRatesForSelectQueryHandler(IImportFclRateRepository importRates)
    : IQueryHandler<GetImportRatesForSelectQuery, Result<IReadOnlyCollection<ImportRateSelectDto>>>
{
    private const string PoeContainsPrefix = "contains:";
    private const string PoeCountryPrefix = "country:";

    public async Task<Result<IReadOnlyCollection<ImportRateSelectDto>>> HandleAsync(
        GetImportRatesForSelectQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var poeContains = ParsePoeContainsFilter(query.Poe);
        if (!string.IsNullOrWhiteSpace(poeContains))
        {
            return await GetPoeContainsRatesAsync(query, poeContains, cancellationToken);
        }

        var approvedExact = await GetExactAsync(query, ImportStatus.Approved, cancellationToken);
        var preAuthorizedExact = await GetExactAsync(query, ImportStatus.PreAuthorized, cancellationToken);

        var exact = approvedExact
            .Concat(preAuthorizedExact)
            .Where(IsSelectableStatus)
            .Where(IsFclRate)
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .OrderBy(x => StatusPriority(x.Status))
            .ThenBy(x => x.Freight)
            .ThenByDescending(x => x.ValidTo)
            .ToArray();

        var requestedDate = query.QuoteDate?.Date;

        // Sin fecha de carga conservamos el comportamiento histórico del selector.
        if (!requestedDate.HasValue && exact.Length > 0)
            return Result.Success<IReadOnlyCollection<ImportRateSelectDto>>(exact);

        // Con fecha de carga debemos mostrar TODAS las tarifas cuya fecha de vencimiento
        // sea igual o posterior a la carga, incluso si su vigencia inicia después de esa fecha.
        // El fallback mantiene POL + POE, tolera POD sin asignar y normaliza equipos como
        // 40HC/40 High Cube, por lo que también recupera esas tarifas futuras compatibles.
        var approvedFallback = await GetFallbackAsync(query, ImportStatus.Approved, cancellationToken);
        var preAuthorizedFallback = await GetFallbackAsync(query, ImportStatus.PreAuthorized, cancellationToken);

        var fallback = approvedFallback
            .Concat(preAuthorizedFallback)
            .Where(x => IsSelectableStatus(x.Status))
            .Where(IsFclRate)
            .Where(x => EquipmentMatches(query.ContainerType, x.ContainerType, x.ContainerTypeCode))
            .Where(x => PodMatchesOrIsUnassigned(query.Pod, x.Pod, x.PodCode, x.PodId))
            .Where(x => !requestedDate.HasValue || x.ValidTo.Date >= requestedDate.Value)
            .Select(ToSelectDto);

        var combined = exact
            .Concat(fallback)
            .Where(x => !requestedDate.HasValue || x.ValidTo.Date >= requestedDate.Value)
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .OrderBy(x => StatusPriority(x.Status))
            .ThenBy(x => x.ValidFrom)
            .ThenBy(x => x.Freight)
            .ThenByDescending(x => x.ValidTo)
            .Take(100)
            .ToArray();

        return Result.Success<IReadOnlyCollection<ImportRateSelectDto>>(combined);
    }

    private async Task<Result<IReadOnlyCollection<ImportRateSelectDto>>> GetPoeContainsRatesAsync(
        GetImportRatesForSelectQuery query,
        string poeContains,
        CancellationToken cancellationToken)
    {
        var approved = await GetAllForPoeContainsAsync(
            query,
            ImportStatus.Approved,
            poeContains,
            cancellationToken
        );
        var preAuthorized = await GetAllForPoeContainsAsync(
            query,
            ImportStatus.PreAuthorized,
            poeContains,
            cancellationToken
        );
        var requestedDate = query.QuoteDate?.Date;

        var rates = approved
            .Concat(preAuthorized)
            .Where(x => IsSelectableStatus(x.Status))
            .Where(IsFclRate)
            .Where(x => EquipmentMatches(query.ContainerType, x.ContainerType, x.ContainerTypeCode))
            .Where(x => PodMatchesOrIsUnassigned(query.Pod, x.Pod, x.PodCode, x.PodId))
            .Where(x => !requestedDate.HasValue || x.ValidTo.Date >= requestedDate.Value)
            .Select(ToSelectDto)
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .OrderBy(x => StatusPriority(x.Status))
            .ThenBy(x => x.ValidFrom)
            .ThenBy(x => x.Freight)
            .ThenByDescending(x => x.ValidTo)
            .ToArray();

        return Result.Success<IReadOnlyCollection<ImportRateSelectDto>>(rates);
    }

    private async Task<IReadOnlyCollection<ImportRateDto>> GetAllForPoeContainsAsync(
        GetImportRatesForSelectQuery query,
        ImportStatus status,
        string poeContains,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var pageNumber = 1;
        var matches = new List<ImportRateDto>();

        while (true)
        {
            var page = await importRates.GetPagedAsync(
                PageRequest.Create(pageNumber, pageSize),
                query.Search,
                query.ImportBatchId,
                query.SourceType,
                status,
                query.Agent,
                query.Carrier,
                query.Pol,
                poe: null,
                pod: null,
                containerType: null,
                currency: query.Currency,
                quoteDate: null,
                validFrom: null,
                validTo: null,
                cancellationToken: cancellationToken
            );

            matches.AddRange(page.Items.Where(rate => PoeContains(rate, poeContains)));

            if (page.Items.Count < pageSize)
                break;

            pageNumber++;
        }

        return matches;
    }

    private async Task<IReadOnlyCollection<ImportRateSelectDto>> GetExactAsync(
        GetImportRatesForSelectQuery query,
        ImportStatus status,
        CancellationToken cancellationToken)
    {
        return await importRates.GetForSelectAsync(
            query.Search,
            query.ImportBatchId,
            query.SourceType,
            status,
            query.Agent,
            query.Carrier,
            query.Pol,
            query.Poe,
            query.Pod,
            query.ContainerType,
            query.Currency,
            query.QuoteDate,
            cancellationToken
        );
    }

    private async Task<IReadOnlyCollection<ImportRateDto>> GetFallbackAsync(
        GetImportRatesForSelectQuery query,
        ImportStatus status,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var pageNumber = 1;
        var matches = new List<ImportRateDto>();

        // No filtramos POE en SQL en esta segunda pasada. Las importaciones históricas pueden
        // guardar el mismo puerto como nombre, UN/LOCODE o slug distinto al catálogo actual.
        // Primero preservamos POL/estado/moneda y luego comparamos todas las representaciones
        // normalizadas del POE para no hacer desaparecer tarifas válidas en Pantalla 5.
        while (true)
        {
            var page = await importRates.GetPagedAsync(
                PageRequest.Create(pageNumber, pageSize),
                query.Search,
                query.ImportBatchId,
                query.SourceType,
                status,
                query.Agent,
                query.Carrier,
                query.Pol,
                poe: null,
                pod: null,
                containerType: null,
                currency: query.Currency,
                quoteDate: null,
                validFrom: null,
                validTo: null,
                cancellationToken: cancellationToken
            );

            matches.AddRange(
                page.Items.Where(rate =>
                    PoeMatches(query.Poe, rate.Poe, rate.PoeCode, rate.PoeSlug)
                )
            );

            if (page.Items.Count < pageSize)
                break;

            pageNumber++;
        }

        return matches;
    }

    private static string? ParsePoeContainsFilter(string? poe)
    {
        if (string.IsNullOrWhiteSpace(poe)) return null;
        var value = poe.Trim();

        if (value.StartsWith(PoeContainsPrefix, StringComparison.OrdinalIgnoreCase))
            return value[PoeContainsPrefix.Length..].Trim();

        if (!value.StartsWith(PoeCountryPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var countryCode = value[PoeCountryPrefix.Length..].Trim();
        return string.Equals(countryCode, "PA", StringComparison.OrdinalIgnoreCase)
            ? "Panama|Panamá"
            : null;
    }

    private static bool PoeContains(ImportRateDto rate, string requested)
    {
        var needles = requested
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CanonicalText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (needles.Length == 0) return true;

        var values = new[] { rate.Poe, rate.PoeCode, rate.PoeSlug }
            .Select(value => CanonicalText(value ?? string.Empty))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (needles.Any(needle => values.Any(value => value.Contains(needle, StringComparison.Ordinal))))
            return true;

        // Los POE de Panamá pueden venir importados únicamente con UN/LOCODE (PABLB,
        // PAMIT, PACOL, etc.). country:PA y contains:Panama deben incluirlos también.
        if (!needles.Contains("panama", StringComparer.Ordinal))
            return false;

        var poeCode = CanonicalText(rate.PoeCode ?? string.Empty);
        return poeCode.Length == 5 && poeCode.StartsWith("pa", StringComparison.Ordinal);
    }

    private static bool PoeMatches(
        string? requestedPoe,
        string? importedPoe,
        string? importedPoeCode,
        string? importedPoeSlug
    )
    {
        if (string.IsNullOrWhiteSpace(requestedPoe)) return true;

        var requestedValues = requestedPoe
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CanonicalText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requestedValues.Length == 0) return true;

        var importedValues = new[] { importedPoe, importedPoeCode, importedPoeSlug }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => CanonicalText(value!))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return requestedValues.Any(requested =>
            importedValues.Any(imported =>
                string.Equals(requested, imported, StringComparison.Ordinal)
                || requested.Contains(imported, StringComparison.Ordinal)
                || imported.Contains(requested, StringComparison.Ordinal)
            )
        );
    }

    private static bool IsSelectableStatus(ImportRateSelectDto rate) => IsSelectableStatus(rate.Status);

    private static bool IsFclRate(ImportRateSelectDto rate) =>
        IsExplicitFclRate(rate.ContainerType, rate.ContainerTypeCode, rate.RawDataJson);

    private static bool IsFclRate(ImportRateDto rate) =>
        IsExplicitFclRate(rate.ContainerType, rate.ContainerTypeCode, rate.RawDataJson);

    private static bool IsExplicitFclRate(string? containerType, string? containerTypeCode, string? rawDataJson)
    {
        if (new[] { containerType, containerTypeCode, rawDataJson }.Any(ContainsLclMarker))
            return false;

        return HasExplicitFclEquipment(containerType, containerTypeCode, rawDataJson);
    }

    private static bool ContainsLclMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("lcl", StringComparison.Ordinal)
            || normalized.Contains("less than container load", StringComparison.Ordinal)
            || normalized.Contains("less-than-container-load", StringComparison.Ordinal)
            || normalized.Contains("coloader", StringComparison.Ordinal)
            || normalized.Contains("co-loader", StringComparison.Ordinal)
            || normalized.Contains("coloading", StringComparison.Ordinal)
            || normalized.Contains("groupage", StringComparison.Ordinal);
    }

    private static bool HasExplicitFclEquipment(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var normalized = CanonicalText(value);
            if (normalized.Contains("fcl", StringComparison.Ordinal)) return true;

            var hasSize = normalized.Contains("20", StringComparison.Ordinal)
                || normalized.Contains("40", StringComparison.Ordinal)
                || normalized.Contains("45", StringComparison.Ordinal);
            var hasEquipmentType = normalized.Contains("highcube", StringComparison.Ordinal)
                || normalized.Contains("dryvan", StringComparison.Ordinal)
                || normalized.Contains("reefer", StringComparison.Ordinal)
                || normalized.Contains("opentop", StringComparison.Ordinal)
                || normalized.Contains("flatrack", StringComparison.Ordinal)
                || normalized.Contains("standard", StringComparison.Ordinal)
                || normalized.Contains("hc", StringComparison.Ordinal)
                || normalized.Contains("hq", StringComparison.Ordinal)
                || normalized.Contains("dv", StringComparison.Ordinal)
                || normalized.Contains("std", StringComparison.Ordinal)
                || normalized.Contains("rf", StringComparison.Ordinal)
                || normalized.Contains("ot", StringComparison.Ordinal)
                || normalized.Contains("fr", StringComparison.Ordinal);

            if (hasSize && hasEquipmentType) return true;
        }

        return false;
    }

    private static bool IsSelectableStatus(string? status) =>
        string.Equals(status, nameof(ImportStatus.Approved), StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, nameof(ImportStatus.PreAuthorized), StringComparison.OrdinalIgnoreCase);

    private static int StatusPriority(string? status) =>
        string.Equals(status, nameof(ImportStatus.Approved), StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private static ImportRateSelectDto ToSelectDto(ImportRateDto rate) =>
        new(
            rate.Id,
            rate.ImportBatchId,
            rate.SourceType,
            rate.Pol,
            rate.Pod,
            rate.Carrier,
            rate.ContainerType,
            rate.Currency,
            rate.Freight,
            rate.FreeDays,
            rate.ValidFrom,
            rate.ValidTo,
            rate.RawDataJson,
            rate.Status,
            rate.UsedAsRateCount,
            rate.PolId,
            rate.PoeId,
            rate.Poe,
            rate.PodId,
            rate.CarrierId,
            rate.ContainerTypeId,
            rate.ContainerTypeCode,
            rate.CurrencyId,
            rate.TotalSale,
            rate.TransitDays,
            rate.SpaceComment
        );

    private static bool PodMatchesOrIsUnassigned(
        string? requestedPod,
        string importedPod,
        string importedPodCode,
        Guid importedPodId
    )
    {
        if (string.IsNullOrWhiteSpace(requestedPod)) return true;

        var imported = CanonicalText(importedPod);
        var importedCode = CanonicalText(importedPodCode);
        if (
            importedPodId == Guid.Empty
            || string.IsNullOrEmpty(imported)
            || imported is "porasignar" or "unassigned" or "pending"
        )
        {
            return true;
        }

        var requested = CanonicalText(requestedPod);
        return requested.Contains(imported, StringComparison.Ordinal)
            || imported.Contains(requested, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(importedCode)
                && requested.Contains(importedCode, StringComparison.Ordinal));
    }

    private static bool EquipmentMatches(string? requestedEquipment, string importedName, string importedCode)
    {
        if (string.IsNullOrWhiteSpace(requestedEquipment)) return true;

        var requested = CanonicalEquipment(requestedEquipment);
        var name = CanonicalEquipment(importedName);
        var code = CanonicalEquipment(importedCode);

        return requested == name
            || requested == code
            || (!string.IsNullOrEmpty(name) && requested.Contains(name, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(code) && requested.Contains(code, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(requested) && name.Contains(requested, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(requested) && code.Contains(requested, StringComparison.Ordinal));
    }

    private static string CanonicalEquipment(string value)
    {
        return CanonicalText(value)
            .Replace("highcube", "hc", StringComparison.Ordinal)
            .Replace("dryvan", "dv", StringComparison.Ordinal)
            .Replace("opentop", "ot", StringComparison.Ordinal)
            .Replace("flatrack", "fr", StringComparison.Ordinal)
            .Replace("reefer", "rf", StringComparison.Ordinal);
    }

    private static string CanonicalText(string value)
    {
        var normalized = value
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(character =>
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark
            )
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();

        return new string(normalized);
    }
}
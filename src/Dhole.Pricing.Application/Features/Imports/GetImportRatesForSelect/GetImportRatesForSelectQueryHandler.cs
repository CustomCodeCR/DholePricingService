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
    public async Task<Result<IReadOnlyCollection<ImportRateSelectDto>>> HandleAsync(
        GetImportRatesForSelectQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var exactResult = await importRates.GetForSelectAsync(
            query.Search,
            query.ImportBatchId,
            query.SourceType,
            ImportStatus.Approved,
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

        IReadOnlyCollection<ImportRateSelectDto> exactApproved = exactResult
            .Where(x => string.Equals(x.Status, nameof(ImportStatus.Approved), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (exactApproved.Count > 0)
            return Result.Success(exactApproved);

        // Si no hay coincidencia estricta, el wizard todavía debe poder proponer una
        // tarifa aprobada que ya fue recibida y no ha vencido. El fallback mantiene
        // POL + POE, tolera POD sin asignar y normaliza equipos como 40HC/40 High Cube.
        var fallbackPage = await importRates.GetPagedAsync(
            PageRequest.Create(1, 100),
            query.Search,
            query.ImportBatchId,
            query.SourceType,
            ImportStatus.Approved,
            query.Agent,
            query.Carrier,
            query.Pol,
            query.Poe,
            pod: null,
            containerType: null,
            currency: query.Currency,
            quoteDate: null,
            validFrom: null,
            validTo: null,
            cancellationToken: cancellationToken
        );

        var requestedDate = query.QuoteDate?.Date;
        var fallbackApproved = fallbackPage.Items
            .Where(x => string.Equals(x.Status, nameof(ImportStatus.Approved), StringComparison.OrdinalIgnoreCase))
            .Where(x => EquipmentMatches(query.ContainerType, x.ContainerType, x.ContainerTypeCode))
            .Where(x => PodMatchesOrIsUnassigned(query.Pod, x.Pod, x.PodCode, x.PodId))
            .Where(x => !requestedDate.HasValue || x.ValidTo.Date >= requestedDate.Value)
            .OrderBy(x => x.ValidFrom)
            .ThenBy(x => x.Freight)
            .Take(100)
            .Select(ToSelectDto)
            .ToArray();

        return Result.Success<IReadOnlyCollection<ImportRateSelectDto>>(fallbackApproved);
    }

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

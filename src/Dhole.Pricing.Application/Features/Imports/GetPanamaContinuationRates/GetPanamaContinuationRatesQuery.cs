using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Features.Imports.GetPanamaContinuationRates;

public sealed record GetPanamaContinuationRatesQuery(
    string PanamaPol,
    string FinalDestination,
    string? ContainerType = null,
    DateTime? QuoteDate = null,
    string? PanamaPolCode = null,
    string? FinalDestinationCode = null
) : IQuery<Result<IReadOnlyCollection<ImportRateSelectDto>>>;

public sealed class GetPanamaContinuationRatesQueryHandler(IImportFclRateRepository importRates)
    : IQueryHandler<GetPanamaContinuationRatesQuery, Result<IReadOnlyCollection<ImportRateSelectDto>>>
{
    public async Task<Result<IReadOnlyCollection<ImportRateSelectDto>>> HandleAsync(
        GetPanamaContinuationRatesQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.PanamaPol) || string.IsNullOrWhiteSpace(query.FinalDestination))
        {
            return Result.Success<IReadOnlyCollection<ImportRateSelectDto>>([]);
        }

        var approved = await GetRatesAsync(query, ImportStatus.Approved, cancellationToken);
        var preAuthorized = await GetRatesAsync(query, ImportStatus.PreAuthorized, cancellationToken);
        var destination = CanonicalText(query.FinalDestination);
        var destinationCode = CanonicalText(query.FinalDestinationCode ?? string.Empty);

        var matches = approved
            .Concat(preAuthorized)
            .Where(rate => DestinationMatches(destination, destinationCode, rate))
            .GroupBy(rate => rate.Id)
            .Select(group => group.First())
            .OrderBy(rate => StatusPriority(rate.Status))
            .ThenBy(rate => rate.Freight)
            .ThenByDescending(rate => rate.ValidTo)
            .Take(100)
            .ToArray();

        return Result.Success<IReadOnlyCollection<ImportRateSelectDto>>(matches);
    }

    private Task<IReadOnlyCollection<ImportRateSelectDto>> GetRatesAsync(
        GetPanamaContinuationRatesQuery query,
        ImportStatus status,
        CancellationToken cancellationToken)
    {
        return importRates.GetForSelectAsync(
            status: status,
            pol: CombineFilter(query.PanamaPol, query.PanamaPolCode),
            containerType: query.ContainerType,
            quoteDate: query.QuoteDate,
            cancellationToken: cancellationToken
        );
    }

    private static bool DestinationMatches(
        string requested,
        string requestedCode,
        ImportRateSelectDto rate)
    {
        if (string.IsNullOrWhiteSpace(requested) && string.IsNullOrWhiteSpace(requestedCode))
            return true;

        return Matches(requested, rate.Poe)
            || Matches(requested, rate.Pod)
            || Matches(requestedCode, rate.PoeCode)
            || Matches(requestedCode, rate.PodCode);
    }

    private static bool Matches(string requested, string? imported)
    {
        if (string.IsNullOrWhiteSpace(requested)) return false;

        var candidate = CanonicalText(imported ?? string.Empty);
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        return requested.Contains(candidate, StringComparison.Ordinal)
            || candidate.Contains(requested, StringComparison.Ordinal);
    }

    private static string CombineFilter(string name, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return name.Trim();

        var normalizedName = name.Trim();
        var normalizedCode = code.Trim();
        if (string.Equals(normalizedName, normalizedCode, StringComparison.OrdinalIgnoreCase))
            return normalizedName;

        return $"{normalizedName}|{normalizedCode}";
    }

    private static int StatusPriority(string? status) =>
        string.Equals(status, nameof(ImportStatus.Approved), StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private static string CanonicalText(string value)
    {
        var normalized = value
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(character =>
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();

        return new string(normalized);
    }
}

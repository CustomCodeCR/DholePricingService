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
    DateTime? QuoteDate = null
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

        var matches = approved
            .Concat(preAuthorized)
            .Where(rate => DestinationMatches(destination, rate))
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
            pol: query.PanamaPol,
            containerType: query.ContainerType,
            quoteDate: query.QuoteDate,
            cancellationToken: cancellationToken
        );
    }

    private static bool DestinationMatches(string requested, ImportRateSelectDto rate)
    {
        if (string.IsNullOrWhiteSpace(requested)) return true;

        return Matches(requested, rate.Poe)
            || Matches(requested, rate.Pod);
    }

    private static bool Matches(string requested, string? imported)
    {
        var candidate = CanonicalText(imported ?? string.Empty);
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        return requested.Contains(candidate, StringComparison.Ordinal)
            || candidate.Contains(requested, StringComparison.Ordinal);
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

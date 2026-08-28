using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.News.Entities;
using Dhole.Pricing.Domain.News.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class LogisticsNewsEndpoints
{
    private const int SpaceCommentMaxLength = 2000;

    private static readonly IReadOnlyDictionary<string, string[]> CarrierAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["emc"] = ["emc", "evergreen", "evergreen marine", "evergreen marine corp"],
            ["evergreen"] = ["emc", "evergreen", "evergreen marine", "evergreen marine corp"],
            ["one"] = ["one", "ocean network express"],
            ["msc"] = ["msc", "mediterranean shipping company"],
            ["maersk"] = ["maersk", "a p moller maersk"],
            ["cma cgm"] = ["cma cgm", "cma-cgm"],
            ["cosco"] = ["cosco", "cosco shipping"],
            ["hmm"] = ["hmm", "hyundai merchant marine"],
            ["zim"] = ["zim", "zim integrated shipping services"],
            ["hapag lloyd"] = ["hapag lloyd", "hapag-lloyd"],
        };

    public static IEndpointRouteBuilder MapLogisticsNewsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/pricing/logistics-news")
            .WithTags("Pricing Logistics News")
            .RequireAuthorization();

        group
            .MapGet("/", ListAsync)
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);

        group
            .MapGet("/{newsId:guid}/impacts", GetImpactsAsync)
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);

        group
            .MapPost("/", CreateAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        group
            .MapPost("/{newsId:guid}/reprocess", ReprocessAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        group
            .MapPut("/{newsId:guid}/active", SetActiveAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        return app;
    }

    private static async Task<IResult> ListAsync(
        bool? active,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        var query = db.Set<LogisticsNews>().AsNoTracking().AsQueryable();
        if (active.HasValue)
        {
            query = query.Where(x => x.IsActive == active.Value);
        }

        var items = await query
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(300)
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);

        return EndpointResults.Ok(items);
    }

    private static async Task<IResult> GetImpactsAsync(
        Guid newsId,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        var impacts = await db.Set<LogisticsNewsRateImpact>()
            .AsNoTracking()
            .Where(x => x.LogisticsNewsId == newsId)
            .OrderByDescending(x => x.AppliedAtUtc)
            .ToListAsync(cancellationToken);

        var rateIds = impacts.Select(x => x.ImportFclRateId).Distinct().ToArray();
        var rates = await db.ImportFclRates
            .AsNoTracking()
            .Where(x => rateIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var result = impacts.Select(impact =>
        {
            rates.TryGetValue(impact.ImportFclRateId, out var rate);
            return new LogisticsNewsImpactDto(
                impact.Id,
                impact.ImportFclRateId,
                rate?.CarrierName ?? rate?.CarrierCode ?? "Naviera",
                rate?.PolName ?? rate?.PolCode ?? "Origen",
                rate?.PoeName ?? rate?.PoeCode ?? "POE",
                rate?.PodName ?? rate?.PodCode ?? "Destino",
                rate?.ContainerTypeName ?? rate?.ContainerTypeCode ?? "Equipo",
                rate?.ValidFrom,
                rate?.ValidTo,
                impact.MatchReason,
                impact.Confidence,
                impact.AppliedComment,
                impact.AppliedAtUtc
            );
        }).ToArray();

        return EndpointResults.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        CreateLogisticsNewsRequest request,
        IHttpClientFactory httpClientFactory,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return EndpointResults.BadRequest(
                "Pricing.LogisticsNews.ContentRequired",
                "El contenido de la noticia logística es requerido.",
                httpContext
            );
        }

        LogisticsNews news;
        try
        {
            news = LogisticsNews.Create(
                request.Title,
                request.Content,
                request.SourceCountry,
                request.SourceOffice,
                request.ReceivedAtUtc,
                httpContext.GetCurrentUserId()
            );
        }
        catch (InvalidOperationException exception)
        {
            return EndpointResults.BadRequest(
                "Pricing.LogisticsNews.Invalid",
                exception.Message,
                httpContext
            );
        }

        db.Set<LogisticsNews>().Add(news);
        await db.SaveChangesAsync(cancellationToken);

        await ProcessNewsAsync(
            news,
            httpClientFactory,
            db,
            httpContext,
            cancellationToken
        );

        return EndpointResults.Ok(ToDto(news));
    }

    private static async Task<IResult> ReprocessAsync(
        Guid newsId,
        IHttpClientFactory httpClientFactory,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var news = await db.Set<LogisticsNews>()
            .FirstOrDefaultAsync(x => x.Id == newsId, cancellationToken);

        if (news is null)
        {
            return Results.NotFound();
        }

        if (!news.IsActive)
        {
            return EndpointResults.BadRequest(
                "Pricing.LogisticsNews.Inactive",
                "Active la noticia antes de reprocesarla.",
                httpContext
            );
        }

        await ProcessNewsAsync(
            news,
            httpClientFactory,
            db,
            httpContext,
            cancellationToken
        );

        return EndpointResults.Ok(ToDto(news));
    }

    private static async Task<IResult> SetActiveAsync(
        Guid newsId,
        SetLogisticsNewsActiveRequest request,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var news = await db.Set<LogisticsNews>()
            .FirstOrDefaultAsync(x => x.Id == newsId, cancellationToken);

        if (news is null)
        {
            return Results.NotFound();
        }

        news.SetActive(request.IsActive, httpContext.GetCurrentUserId());
        await db.SaveChangesAsync(cancellationToken);
        return EndpointResults.Ok(ToDto(news));
    }

    private static async Task ProcessNewsAsync(
        LogisticsNews news,
        IHttpClientFactory httpClientFactory,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var userId = httpContext.GetCurrentUserId();

        try
        {
            var analysis = await AnalyzeWithAiAsync(
                news,
                httpClientFactory,
                httpContext,
                cancellationToken
            );

            var analysisJson = JsonSerializer.Serialize(analysis);
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var currentApprovedRates = await db.ImportFclRates
                .Where(rate =>
                    rate.Status == ImportStatus.Approved
                    && rate.ValidFrom < tomorrow
                    && rate.ValidTo >= today
                )
                .ToListAsync(cancellationToken);

            var matched = currentApprovedRates
                .Where(rate => IsMatch(rate, analysis))
                .ToList();

            var existingRateIds = await db.Set<LogisticsNewsRateImpact>()
                .Where(x => x.LogisticsNewsId == news.Id)
                .Select(x => x.ImportFclRateId)
                .ToHashSetAsync(cancellationToken);

            foreach (var rate in matched.Where(rate => !existingRateIds.Contains(rate.Id)))
            {
                var appliedComment = BuildNewsComment(news, analysis);
                var combinedComment = CombineSpaceComment(rate.SpaceComment, appliedComment);
                db.Entry(rate).Property(nameof(ImportFclRates.SpaceComment)).CurrentValue = combinedComment;

                db.Set<LogisticsNewsRateImpact>().Add(
                    LogisticsNewsRateImpact.Create(
                        news.Id,
                        rate.Id,
                        BuildMatchReason(rate, analysis),
                        analysis.Confidence,
                        appliedComment
                    )
                );
                existingRateIds.Add(rate.Id);
            }

            var totalApplied = existingRateIds.Count;
            news.MarkProcessed(
                analysis.Summary,
                analysisJson,
                analysis.EventType,
                analysis.Severity,
                analysis.Confidence,
                matched.Count,
                totalApplied,
                userId
            );

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            news.MarkFailed(exception.Message, userId);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<LogisticsNewsAiAnalysis> AnalyzeWithAiAsync(
        LogisticsNews news,
        IHttpClientFactory httpClientFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var client = httpClientFactory.CreateClient("DholeAI");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/ai/logistics/news/analyze")
        {
            Content = JsonContent.Create(new
            {
                content = news.Content,
                title = news.Title,
                sourceCountry = news.SourceCountry,
            }),
        };

        var authorization = httpContext.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"La IA no pudo analizar la noticia logística (HTTP {(int)response.StatusCode})."
            );
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var payload = root.TryGetProperty("data", out var data) ? data : root;

        var analysis = JsonSerializer.Deserialize<LogisticsNewsAiAnalysis>(
            payload.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (analysis is null || string.IsNullOrWhiteSpace(analysis.RecommendedObservation))
        {
            throw new InvalidOperationException("La IA devolvió una noticia logística sin estructura válida.");
        }

        return analysis with
        {
            CarrierTerms = CleanTerms(analysis.CarrierTerms),
            OriginTerms = CleanTerms(analysis.OriginTerms),
            DestinationTerms = CleanTerms(analysis.DestinationTerms),
            Confidence = Math.Clamp(analysis.Confidence, 0m, 1m),
        };
    }

    private static bool IsMatch(ImportFclRates rate, LogisticsNewsAiAnalysis analysis)
    {
        var carrierTerms = ExpandCarrierTerms(analysis.CarrierTerms);
        var hasCarrierFilter = carrierTerms.Length > 0;
        var hasOriginFilter = analysis.OriginTerms.Length > 0;
        var hasDestinationFilter = analysis.DestinationTerms.Length > 0;

        if (!hasCarrierFilter && !hasOriginFilter && !hasDestinationFilter)
        {
            return false;
        }

        if (hasCarrierFilter && !MatchesAny(
                carrierTerms,
                rate.Carrier,
                rate.CarrierName,
                rate.CarrierCode,
                rate.CarrierSlug
            ))
        {
            return false;
        }

        if (hasOriginFilter && !MatchesAny(
                analysis.OriginTerms,
                rate.Pol,
                rate.PolName,
                rate.PolCode,
                rate.PolSlug
            ))
        {
            return false;
        }

        if (hasDestinationFilter && !MatchesAny(
                analysis.DestinationTerms,
                rate.Poe,
                rate.PoeName,
                rate.PoeCode,
                rate.PoeSlug,
                rate.Pod,
                rate.PodName,
                rate.PodCode,
                rate.PodSlug
            ))
        {
            return false;
        }

        return true;
    }

    private static string[] ExpandCarrierTerms(IEnumerable<string>? terms)
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in CleanTerms(terms))
        {
            expanded.Add(term);
            var normalized = Normalize(term);
            if (CarrierAliases.TryGetValue(normalized, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    expanded.Add(alias);
                }
            }
        }

        return expanded.ToArray();
    }

    private static bool MatchesAny(IEnumerable<string> terms, params string?[] values)
    {
        var normalizedValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Normalize(value!))
            .Where(value => value.Length > 0)
            .ToArray();

        foreach (var term in terms)
        {
            var normalizedTerm = Normalize(term);
            if (normalizedTerm.Length < 2)
            {
                continue;
            }

            if (normalizedValues.Any(value =>
                    value.Equals(normalizedTerm, StringComparison.OrdinalIgnoreCase)
                    || (normalizedTerm.Length >= 3 && value.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase))
                    || (value.Length >= 3 && normalizedTerm.Contains(value, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return string.Join(
            ' ',
            builder.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token is not "port" and not "puerto" and not "de" and not "del" and not "the")
        );
    }

    private static string[] CleanTerms(IEnumerable<string>? terms)
    {
        return (terms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static string BuildNewsComment(
        LogisticsNews news,
        LogisticsNewsAiAnalysis analysis
    )
    {
        var source = string.IsNullOrWhiteSpace(news.SourceCountry)
            ? string.Empty
            : $" Fuente: {news.SourceCountry}.";
        var prefix = $"[Noticia logística {news.ReceivedAtUtc:dd/MM/yyyy}]";
        var comment = $"{prefix} {analysis.RecommendedObservation.Trim()}{source}";
        return comment.Length <= SpaceCommentMaxLength
            ? comment
            : comment[..SpaceCommentMaxLength];
    }

    private static string CombineSpaceComment(string? existing, string newsComment)
    {
        var normalizedExisting = string.IsNullOrWhiteSpace(existing) ? null : existing.Trim();
        if (normalizedExisting is null)
        {
            return newsComment.Length <= SpaceCommentMaxLength
                ? newsComment
                : newsComment[..SpaceCommentMaxLength];
        }

        var availableForExisting = Math.Max(0, SpaceCommentMaxLength - newsComment.Length - 1);
        if (normalizedExisting.Length > availableForExisting)
        {
            normalizedExisting = normalizedExisting[..availableForExisting].TrimEnd();
        }

        return $"{normalizedExisting}\n{newsComment}";
    }

    private static string BuildMatchReason(
        ImportFclRates rate,
        LogisticsNewsAiAnalysis analysis
    )
    {
        var route = $"{rate.PolName} → {rate.PoeName} → {rate.PodName}";
        return $"Coincidencia IA validada por backend: {rate.CarrierName}; {route}; evento {analysis.EventType}; severidad {analysis.Severity}.";
    }

    private static LogisticsNewsDto ToDto(LogisticsNews news)
    {
        return new LogisticsNewsDto(
            news.Id,
            news.Title,
            news.Content,
            news.SourceCountry,
            news.SourceOffice,
            news.ReceivedAtUtc,
            news.Status.ToString(),
            news.IsActive,
            news.AiSummary,
            news.EventType,
            news.Severity,
            news.AiConfidence,
            news.MatchedRateCount,
            news.AppliedRateCount,
            news.LastProcessedAtUtc,
            news.ProcessingError
        );
    }
}

public sealed record CreateLogisticsNewsRequest(
    string Content,
    string? Title,
    string? SourceCountry,
    string? SourceOffice,
    DateTime? ReceivedAtUtc
);

public sealed record SetLogisticsNewsActiveRequest(bool IsActive);

public sealed record LogisticsNewsDto(
    Guid Id,
    string Title,
    string Content,
    string? SourceCountry,
    string? SourceOffice,
    DateTime ReceivedAtUtc,
    string Status,
    bool IsActive,
    string? AiSummary,
    string? EventType,
    string? Severity,
    decimal? AiConfidence,
    int MatchedRateCount,
    int AppliedRateCount,
    DateTime? LastProcessedAtUtc,
    string? ProcessingError
);

public sealed record LogisticsNewsImpactDto(
    Guid Id,
    Guid ImportFclRateId,
    string Carrier,
    string Pol,
    string Poe,
    string Pod,
    string ContainerType,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    string MatchReason,
    decimal Confidence,
    string AppliedComment,
    DateTime AppliedAtUtc
);

public sealed record LogisticsNewsAiAnalysis(
    string Summary,
    string[] CarrierTerms,
    string[] OriginTerms,
    string[] DestinationTerms,
    string EventType,
    string Severity,
    string RecommendedObservation,
    decimal Confidence
);

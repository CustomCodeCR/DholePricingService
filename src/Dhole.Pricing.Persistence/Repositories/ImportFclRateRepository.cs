using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class ImportFclRateRepository(ServiceDbContext dbContext, IConfiguration configuration)
    : EfRepository<ImportFclRates, Guid>(dbContext),
        IImportFclRateRepository
{
    public async Task<IReadOnlyCollection<ImportFclRates>> GetByImportFclBatchIdAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .ImportFclRates.AsNoTracking()
            .Where(x => x.ImportBatchId == importBatchId && !x.IsDeleted)
            .OrderBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ImportFclRates>> GetPendingByImportFclBatchIdAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .ImportFclRates.AsNoTracking()
            .Where(x =>
                x.ImportBatchId == importBatchId && x.Status == ImportStatus.Pending && !x.IsDeleted
            )
            .OrderBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsCreatedRateFclAsync(
        Guid importFclRateId,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext.ImportFclRates.AnyAsync(
            x =>
                x.Id == importFclRateId
                && !x.IsDeleted
                && (x.CreatedAsRateHeaderId.HasValue || x.UsedAsRateCount > 0),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<ImportFclRates>> GetValidImportedRatesFclAsync(
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? pol = null,
        string? pod = null,
        string? carrier = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateTime.UtcNow.Date;
        await ExpireOutdatedAsync(today, cancellationToken);

        var query = ApplyFilters(
            dbContext
                .ImportFclRates.AsNoTracking()
                .Where(x =>
                    !x.IsDeleted
                    && x.Status != ImportStatus.Expired
                    && x.ValidFrom.Date <= today
                    && x.ValidTo.Date >= today
                ),
            search: null,
            importBatchId: null,
            sourceType: sourceType,
            status: status,
            agent: null,
            carrier: carrier,
            pol: pol,
            poe: null,
            pod: pod,
            containerType: containerType,
            currency: currency,
            quoteDate: quoteDate,
            validFrom: null,
            validTo: null
        );

        return await query
            .OrderBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .ThenBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<ImportRateDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        Guid? importBatchId = null,
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? agent = null,
        string? carrier = null,
        string? pol = null,
        string? poe = null,
        string? pod = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        DateTime? validFrom = null,
        DateTime? validTo = null,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateTime.UtcNow.Date;
        await ExpireOutdatedAsync(today, cancellationToken);

        var query = ApplyFilters(
            dbContext
                .ImportFclRates.AsNoTracking()
                .Where(x =>
                    !x.IsDeleted
                    && x.Status != ImportStatus.Expired
                    && x.ValidTo.Date >= today
                ),
            search,
            importBatchId,
            sourceType,
            status,
            agent,
            carrier,
            pol,
            poe,
            pod,
            containerType,
            currency,
            quoteDate: quoteDate,
            validFrom: validFrom,
            validTo: validTo
        );

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.ValidFrom)
            .ThenBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => new ImportRateDto
            {
                Id = x.Id,
                ImportBatchId = x.ImportBatchId,
                ExtractionRecordId = x.ExtractionRecordId,
                SourceType = x.SourceType.ToString(),
                ImportProfileId = x.ImportProfileId,
                ImportProfileName = x.ImportProfileName,
                ImportProfileCode = x.ImportProfileCode,
                ImportProfileSlug = x.ImportProfileSlug,
                PolId = x.PolId,
                Pol = x.PolName,
                PolCode = x.PolCode,
                PolSlug = x.PolSlug,
                PoeId = x.PoeId,
                Poe = x.PoeName,
                PoeCode = x.PoeCode,
                PoeSlug = x.PoeSlug,
                PodId = x.PodId,
                Pod = x.PodName,
                PodCode = x.PodCode,
                PodSlug = x.PodSlug,
                CarrierId = x.CarrierId,
                Carrier = x.CarrierName,
                CarrierCode = x.CarrierCode,
                CarrierSlug = x.CarrierSlug,
                AgentId = x.AgentId,
                Agent = x.AgentName,
                AgentCode = x.AgentCode,
                AgentSlug = x.AgentSlug,
                ContainerTypeId = x.ContainerTypeId,
                ContainerType = x.ContainerTypeName,
                ContainerTypeCode = x.ContainerTypeCode,
                ContainerTypeSlug = x.ContainerTypeSlug,
                CurrencyId = x.CurrencyId,
                Currency = x.CurrencyName,
                CurrencyCode = x.CurrencyCode,
                CurrencySlug = x.CurrencySlug,
                Commodity = x.Commodity,
                SpaceComment = x.SpaceComment,
                Freight = x.OceanFreight ?? x.Freight,
                OceanFreight = x.OceanFreight,
                OriginCharges = x.OriginCharges,
                DestinationCharges = x.DestinationCharges,
                Surcharges = x.Surcharges,
                TotalCost = x.TotalCost,
                TotalSale = x.TotalSale,
                Profit = x.Profit,
                Margin = x.Margin,
                FreeDays = x.FreeDays,
                TransitDays = x.TransitDays,
                ValidFrom = x.ValidFrom,
                ValidTo = x.ValidTo,
                RawDataJson = x.RawDataJson ?? "{}",
                Status = x.Status.ToString(),
                UsedAsRateCount = x.UsedAsRateCount,
                CreatedAsRateHeaderId = x.CreatedAsRateHeaderId,
            })
            .ToListAsync(cancellationToken);

        return PagedResult<ImportRateDto>.Create(items, page.PageNumber, page.PageSize, total);
    }

    public async Task<PricingDecisionDashboardDto> GetDecisionDashboardAsync(
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string? containerType = null,
        CancellationToken cancellationToken = default
    )
    {
        const decimal multimodalLandFreight = 2140m;
        var prioritySettings = ReadPrioritySettings();

        var today = DateTime.UtcNow.Date;
        await ExpireOutdatedAsync(today, cancellationToken);

        var startDate = dateFrom?.Date;
        var endDateExclusive = dateTo?.Date.AddDays(1);

        var query = dbContext
            .ImportFclRates.AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && x.Status != ImportStatus.Rejected
                && x.Status != ImportStatus.Expired
                && x.ValidFrom.Date <= today
                && x.ValidTo.Date >= today
            );

        if (startDate.HasValue)
            query = query.Where(x => x.ValidFrom >= startDate.Value);

        if (endDateExclusive.HasValue)
            query = query.Where(x => x.ValidTo < endDateExclusive.Value);

        if (!string.IsNullOrWhiteSpace(containerType))
            query = query.Where(x => x.ContainerTypeName.Contains(containerType));

        query = query.Where(x =>
            (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("limon")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("limón")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("moin")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("moín")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("caldera")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("manzanillo")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("colon")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("colón")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("rodman")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("cristobal")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("cristóbal")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("panama")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe).ToLower().Contains("panamá")
        );

        var importedRates = await query
            .OrderBy(x => x.CarrierName)
            .ThenBy(x => x.ContainerTypeName)
            .ThenBy(x => x.PolName)
            .Select(x => new
            {
                x.Id,
                x.ImportBatchId,
                x.CarrierName,
                OceanFreight = x.OceanFreight ?? x.Freight,
                x.TotalSale,
                x.Margin,
                Currency = x.CurrencyName,
                x.ContainerTypeName,
                x.PolName,
                x.PoeName,
                x.ValidFrom,
                x.ValidTo,
                x.Status,
                x.SpaceComment,
                x.RawDataJson,
            })
            .ToListAsync(cancellationToken);

        var candidates = importedRates
            .Select(rate =>
            {
                var lane = ResolveDecisionLane(rate.PoeName);
                var comment = string.IsNullOrWhiteSpace(rate.SpaceComment)
                    ? ExtractSpaceComment(rate.RawDataJson)
                    : rate.SpaceComment.Trim();
                var space = EvaluateSpaceComment(comment, prioritySettings);
                return new DecisionCandidate(
                    rate.Id, rate.ImportBatchId, rate.CarrierName, rate.OceanFreight,
                    lane == DecisionLane.Multimodal ? multimodalLandFreight : null,
                    rate.TotalSale, rate.Margin, rate.Currency, rate.ContainerTypeName,
                    rate.PolName, rate.PoeName, rate.ValidFrom, rate.ValidTo, rate.Status.ToString(), lane,
                    comment, space.Score, space.Risk, space.Reason
                );
            })
            .Where(x => x.Lane.HasValue)
            .ToArray();

        var scored = new List<(DecisionLane Lane, PricingDecisionRateDto Rate)>();

        // El HTML de decisión FCL calcula precio y margen contra el universo comparable
        // actualmente filtrado. Aquí hacemos lo mismo por moneda para no mezclar importes
        // que no son directamente comparables, pero NO reiniciamos el score por POE/vía.
        foreach (var currencyGroup in candidates.GroupBy(x => x.Currency, StringComparer.OrdinalIgnoreCase))
        {
            var comparable = currencyGroup
                .Select(x => (x.TotalSale ?? x.OceanFreight) + (x.LandFreight ?? 0m))
                .Where(x => x > 0m)
                .ToArray();
            var minimumPrice = comparable.Length == 0 ? 0m : comparable.Min();
            var maxMargin = currencyGroup
                .Select(x => Math.Max(0m, x.Margin ?? 0m))
                .DefaultIfEmpty(0m)
                .Max();

            foreach (var candidate in currencyGroup)
            {
                var amount = (candidate.TotalSale ?? candidate.OceanFreight)
                    + (candidate.LandFreight ?? 0m);

                // Misma fórmula del HTML:
                // 100 - ((venta - venta mínima) / max(venta mínima, 1)) * 100.
                var priceScore = amount > 0m && minimumPrice > 0m
                    ? Math.Max(
                        0m,
                        100m - ((amount - minimumPrice) / Math.Max(minimumPrice, 1m)) * 100m
                    )
                    : 0m;
                var marginScore = maxMargin > 0m
                    ? Math.Clamp(
                        (Math.Max(0m, candidate.Margin ?? 0m) / maxMargin) * 100m,
                        0m,
                        100m
                    )
                    : 0m;
                var totalWeight = prioritySettings.SpaceWeight
                    + prioritySettings.PriceWeight
                    + prioritySettings.MarginWeight;
                var priority = totalWeight > 0m
                    ? decimal.Round(
                        (candidate.SpaceScore * prioritySettings.SpaceWeight
                            + priceScore * prioritySettings.PriceWeight
                            + marginScore * prioritySettings.MarginWeight) / totalWeight,
                        2
                    )
                    : 0m;
                var reason = $"Espacios {candidate.SpaceScore:0}/100 ({AsPercent(prioritySettings.SpaceWeight, totalWeight):0.#}%) · "
                    + $"precio {priceScore:0}/100 ({AsPercent(prioritySettings.PriceWeight, totalWeight):0.#}%) · "
                    + $"margen {marginScore:0}/100 ({AsPercent(prioritySettings.MarginWeight, totalWeight):0.#}%). "
                    + candidate.SpaceReason;

                scored.Add((
                    candidate.Lane!.Value,
                    new PricingDecisionRateDto(
                        candidate.Id, candidate.ImportBatchId, candidate.Carrier,
                        candidate.OceanFreight, candidate.LandFreight, candidate.Currency,
                        candidate.ContainerType, candidate.Pol, candidate.Poe, candidate.ValidFrom,
                        candidate.ValidTo, candidate.Status, candidate.TotalSale, candidate.Margin, candidate.SpaceComment,
                        candidate.SpaceScore, candidate.SpaceRisk, priority, reason
                    )
                ));
            }
        }

        IReadOnlyCollection<PricingDecisionRateDto> RatesFor(DecisionLane lane) => scored
            .Where(x => x.Lane == lane)
            .Select(x => x.Rate)
            .OrderByDescending(x => x.PriorityScore)
            .ThenBy(x => (x.TotalSale ?? x.InternationalOceanFreight) + (x.InternationalLandFreight ?? 0m))
            .ThenBy(x => x.Carrier)
            .ToArray();

        var limonMoinRates = RatesFor(DecisionLane.LimonMoin);
        var calderaRates = RatesFor(DecisionLane.Caldera);
        var multimodalRates = RatesFor(DecisionLane.Multimodal);

        var lanes = new PricingDecisionLaneDto[]
        {
            new("limon-moin", "Limón / Moín", "Entrada directa por Limón o Moín.", limonMoinRates.Count, limonMoinRates),
            new("puerto-caldera", "Puerto Caldera", "Entrada directa por Puerto Caldera.", calderaRates.Count, calderaRates),
            new("multimodal", "Multimodal", $"Entrada por Panamá + terrestre internacional de USD {multimodalLandFreight:0}.", multimodalRates.Count, multimodalRates),
        };

        return new PricingDecisionDashboardDto(
            startDate,
            dateTo?.Date,
            multimodalLandFreight,
            lanes.Sum(x => x.TotalOptions),
            lanes
        );
    }

    private static string ExtractSpaceComment(string? rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(rawDataJson)) return string.Empty;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(rawDataJson);
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "COMENTARIOS DE ESPACIOS", "comentariosDeEspacios", "spaceComments",
                "spaceComment", "comentarioEspacios", "comentarioDeEspacios",
                "remarks", "observaciones", "comentarios"
            };
            string? Visit(System.Text.Json.JsonElement element)
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var property in element.EnumerateObject())
                    {
                        if (aliases.Contains(property.Name) && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var text = property.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                        }
                        var nested = Visit(property.Value);
                        if (!string.IsNullOrWhiteSpace(nested)) return nested;
                    }
                }
                else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var child in element.EnumerateArray())
                    {
                        var nested = Visit(child);
                        if (!string.IsNullOrWhiteSpace(nested)) return nested;
                    }
                }
                return null;
            }
            return Visit(document.RootElement) ?? string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }

    private static SpaceEvaluation EvaluateSpaceComment(string comment, DecisionPrioritySettings settings)
    {
        var normalized = RemoveDiacritics(comment).ToLowerInvariant();
        var score = settings.SpaceBaseScore;
        var hits = new List<string>();

        var positive = new[] { "libero espacio", "se libero espacio", "espacio liberado", "booking confirmado", "cupo confirmado", "hay espacio", "space available", "espacio confirmado" };
        var alerts = new[] { "sujeto", "revisar", "validar", "pendiente", "limitado", "poco espacio", "consultar" };
        var risks = new[] { "sin espacio", "no hay espacio", "roll", "rolleo", "congestion", "lleno", "full", "dificil", "no libero" };

        var riskMatches = risks.Where(normalized.Contains).Distinct(StringComparer.Ordinal).ToArray();
        var alertMatches = alerts.Where(normalized.Contains).Distinct(StringComparer.Ordinal).ToArray();
        var positiveMatches = positive
            .Where(normalized.Contains)
            .Where(word => !(word == "hay espacio" && normalized.Contains("no hay espacio")))
            .Where(word => !(word == "libero espacio" && normalized.Contains("no libero")))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var word in positiveMatches) { score += settings.SpacePositiveAdjustment; hits.Add($"positivo: {word}"); }
        foreach (var word in alertMatches) { score += settings.SpaceAlertAdjustment; hits.Add($"alerta: {word}"); }
        foreach (var word in riskMatches) { score += settings.SpaceRiskAdjustment; hits.Add($"riesgo: {word}"); }

        score = Math.Clamp(score, 0m, 100m);
        var risk = score >= 75m ? "Bajo" : score >= 45m ? "Medio" : "Alto";
        if (hits.Count == 0) hits.Add(string.IsNullOrWhiteSpace(comment) ? "sin comentario operativo" : "sin señales específicas detectadas");
        return new SpaceEvaluation(score, risk, string.Join(" · ", hits.Take(3)));
    }

    private DecisionPrioritySettings ReadPrioritySettings()
    {
        decimal Read(string key, decimal fallback) =>
            configuration.GetValue<decimal?>($"Pricing:DecisionPriority:{key}") ?? fallback;

        return new DecisionPrioritySettings(
            Math.Max(0m, Read("SpaceWeight", 0.50m)),
            Math.Max(0m, Read("PriceWeight", 0.30m)),
            Math.Max(0m, Read("MarginWeight", 0.20m)),
            Math.Clamp(Read("SpaceBaseScore", 50m), 0m, 100m),
            Read("SpacePositiveAdjustment", 28m),
            Read("SpaceAlertAdjustment", -8m),
            Read("SpaceRiskAdjustment", -30m)
        );
    }

    private static decimal AsPercent(decimal weight, decimal totalWeight) =>
        totalWeight > 0m ? weight / totalWeight * 100m : 0m;

    private sealed record DecisionPrioritySettings(
        decimal SpaceWeight,
        decimal PriceWeight,
        decimal MarginWeight,
        decimal SpaceBaseScore,
        decimal SpacePositiveAdjustment,
        decimal SpaceAlertAdjustment,
        decimal SpaceRiskAdjustment
    );

    private sealed record SpaceEvaluation(decimal Score, string Risk, string Reason);
    private sealed record DecisionCandidate(
        Guid Id, Guid ImportBatchId, string Carrier, decimal OceanFreight, decimal? LandFreight,
        decimal? TotalSale, decimal? Margin, string Currency, string ContainerType, string Pol, string Poe,
        DateTime ValidFrom, DateTime ValidTo, string Status, DecisionLane? Lane, string SpaceComment, decimal SpaceScore,
        string SpaceRisk, string SpaceReason
    );

    private static DecisionLane? ResolveDecisionLane(string poe)
    {
        var value = RemoveDiacritics(poe).ToLowerInvariant();

        if (value.Contains("limon") || value.Contains("moin"))
        {
            return DecisionLane.LimonMoin;
        }

        if (value.Contains("caldera"))
        {
            return DecisionLane.Caldera;
        }

        if (
            value.Contains("manzanillo")
            || value.Contains("colon")
            || value.Contains("rodman")
            || value.Contains("cristobal")
            || value.Contains("panama")
        )
        {
            return DecisionLane.Multimodal;
        }

        return null;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark
            )
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private enum DecisionLane
    {
        LimonMoin,
        Caldera,
        Multimodal,
    }

    public async Task<IReadOnlyCollection<ImportRateSelectDto>> GetForSelectAsync(
        string? search = null,
        Guid? importBatchId = null,
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? agent = null,
        string? carrier = null,
        string? pol = null,
        string? poe = null,
        string? pod = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateTime.UtcNow.Date;
        await ExpireOutdatedAsync(today, cancellationToken);

        var query = ApplyFilters(
            dbContext
                .ImportFclRates.AsNoTracking()
                .Where(x =>
                    !x.IsDeleted
                    && x.Status != ImportStatus.Expired
                    && x.ValidFrom.Date <= today
                    && x.ValidTo.Date >= today
                ),
            search,
            importBatchId,
            sourceType,
            status,
            agent,
            carrier,
            pol,
            poe,
            pod,
            containerType,
            currency,
            quoteDate: quoteDate,
            validFrom: null,
            validTo: null
        );

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .Take(100)
            .Select(x => new ImportRateSelectDto(
                x.Id,
                x.ImportBatchId,
                x.SourceType.ToString(),
                x.PolName,
                x.PodName,
                x.CarrierName,
                x.ContainerTypeName,
                x.CurrencyName,
                x.OceanFreight ?? x.Freight,
                x.FreeDays,
                x.ValidFrom,
                x.ValidTo,
                x.RawDataJson ?? "{}",
                x.Status.ToString(),
                x.UsedAsRateCount,
                x.PolId,
                x.PoeId,
                x.PoeName,
                x.PodId,
                x.CarrierId,
                x.ContainerTypeId,
                x.ContainerTypeCode,
                x.CurrencyId,
                x.TotalSale,
                x.TransitDays,
                x.SpaceComment
            ))
            .ToListAsync(cancellationToken);
    }

    private Task<int> ExpireOutdatedAsync(DateTime today, CancellationToken cancellationToken)
    {
        return dbContext
            .ImportFclRates.Where(x =>
                !x.IsDeleted && x.Status != ImportStatus.Expired && x.ValidTo.Date < today.Date
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Status, ImportStatus.Expired)
                        .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken
            );
    }

    private static IQueryable<ImportFclRates> ApplyFilters(
        IQueryable<ImportFclRates> query,
        string? search,
        Guid? importBatchId,
        ImportSourceType? sourceType,
        ImportStatus? status,
        string? agent,
        string? carrier,
        string? pol,
        string? poe,
        string? pod,
        string? containerType,
        string? currency,
        DateTime? quoteDate,
        DateTime? validFrom,
        DateTime? validTo
    )
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = NormalizeSearchValue(search);

            query = query.Where(x =>
                x.Pol.ToLower().Contains(value)
                || x.PolName.ToLower().Contains(value)
                || x.Poe.ToLower().Contains(value)
                || x.PoeName.ToLower().Contains(value)
                || x.Pod.ToLower().Contains(value)
                || x.PodName.ToLower().Contains(value)
                || x.Carrier.ToLower().Contains(value)
                || x.CarrierName.ToLower().Contains(value)
                || x.Agent.ToLower().Contains(value)
                || x.AgentName.ToLower().Contains(value)
                || x.ContainerType.ToLower().Contains(value)
                || x.ContainerTypeName.ToLower().Contains(value)
                || x.Currency.ToLower().Contains(value)
                || x.CurrencyName.ToLower().Contains(value)
                || x.SourceType.ToString().ToLower().Contains(value)
                || x.Status.ToString().ToLower().Contains(value)
            );
        }

        if (importBatchId.HasValue)
        {
            query = query.Where(x => x.ImportBatchId == importBatchId.Value);
        }

        if (sourceType.HasValue)
        {
            query = query.Where(x => x.SourceType == sourceType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(agent))
        {
            var (primary, secondary) = ParseFilterValues(agent);

            query = query.Where(x =>
                x.AgentCode.ToLower().Contains(primary)
                || x.AgentName.ToLower().Contains(primary)
                || (secondary != null && x.AgentCode.ToLower().Contains(secondary))
                || (secondary != null && x.AgentName.ToLower().Contains(secondary))
            );
        }

        if (!string.IsNullOrWhiteSpace(carrier))
        {
            var (primary, secondary) = ParseFilterValues(carrier);

            query = query.Where(x =>
                x.CarrierCode.ToLower().Contains(primary)
                || x.CarrierName.ToLower().Contains(primary)
                || (secondary != null && x.CarrierCode.ToLower().Contains(secondary))
                || (secondary != null && x.CarrierName.ToLower().Contains(secondary))
            );
        }

        if (!string.IsNullOrWhiteSpace(pol))
        {
            var (primary, secondary) = ParseFilterValues(pol);

            query = query.Where(x =>
                primary.ToLower().Contains(x.PolCode.ToLower())
                || primary.ToLower().Contains(x.PolName.ToLower())
                || (secondary != null && secondary.ToLower().Contains(x.PolCode.ToLower()))
                || (secondary != null && secondary.ToLower().Contains(x.PolName.ToLower()))
            );
        }

        if (!string.IsNullOrWhiteSpace(poe))
        {
            var (primary, secondary) = ParseFilterValues(poe);

            query = query.Where(x =>
                primary.ToLower().Contains(x.PoeCode.ToLower())
                || primary.ToLower().Contains(x.PoeName.ToLower())
                || (secondary != null && secondary.ToLower().Contains(x.PoeCode.ToLower()))
                || (secondary != null && secondary.ToLower().Contains(x.PoeName.ToLower()))
            );
        }

        if (!string.IsNullOrWhiteSpace(pod))
        {
            var (primary, secondary) = ParseFilterValues(pod);

            query = query.Where(x =>
                primary.ToLower().Contains(x.PodCode.ToLower())
                || primary.ToLower().Contains(x.PodName.ToLower())
                || (secondary != null && secondary.ToLower().Contains(x.PodCode.ToLower()))
                || (secondary != null && secondary.ToLower().Contains(x.PodName.ToLower()))
            );
        }

        if (!string.IsNullOrWhiteSpace(containerType))
        {
            var (primary, secondary) = ParseFilterValues(containerType);

            query = query.Where(x =>
                x.ContainerTypeCode.ToLower().Contains(primary)
                || x.ContainerTypeName.ToLower().Contains(primary)
                || (secondary != null && x.ContainerTypeCode.ToLower().Contains(secondary))
                || (secondary != null && x.ContainerTypeName.ToLower().Contains(secondary))
            );
        }

        if (!string.IsNullOrWhiteSpace(currency))
        {
            var (primary, secondary) = ParseFilterValues(currency);

            query = query.Where(x =>
                x.CurrencyCode.ToLower().Contains(primary)
                || x.CurrencyName.ToLower().Contains(primary)
                || (secondary != null && x.CurrencyCode.ToLower().Contains(secondary))
                || (secondary != null && x.CurrencyName.ToLower().Contains(secondary))
            );
        }

        if (quoteDate.HasValue)
        {
            var value = quoteDate.Value.Date;

            query = query.Where(x => x.ValidFrom.Date <= value && x.ValidTo.Date >= value);
        }

        if (validFrom.HasValue)
        {
            query = query.Where(x => x.ValidFrom.Date >= validFrom.Value.Date);
        }

        if (validTo.HasValue)
        {
            query = query.Where(x => x.ValidTo.Date <= validTo.Value.Date);
        }

        return query;
    }

    private static string NormalizeSearchValue(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static (string Primary, string? Secondary) ParseFilterValues(string value)
    {
        var values = value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSearchValue)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        var primary = values.FirstOrDefault() ?? NormalizeSearchValue(value);

        return (primary, values.Length > 1 ? values[1] : null);
    }
}

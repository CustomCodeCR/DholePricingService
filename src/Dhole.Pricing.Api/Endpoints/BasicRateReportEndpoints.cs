using System.Globalization;
using System.Text.Json;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Basic quotation ledger, filtered by Costa Rica creation date (UTC-06:00).
/// ReportsService generates the Excel; Pricing remains the source of financial truth.
/// </summary>
public static class BasicRateReportEndpoints
{
    private const string SpreadsheetMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapBasicRateReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rates")
            .WithTags("Basic quotation report")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestViewAll);

        group.MapGet("/basic-report", GetAsync);
        group.MapGet("/basic-report.xlsx", ExportAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(
        string? from, string? to, string? search,
        ServiceDbContext db,
        IPricingConfigCatalogClient catalog,
        SellerVisibilityService visibility,
        HttpContext context,
        CancellationToken ct)
    {
        if (!visibility.HasScope(context.User, PricingConstants.Scopes.RateRequestViewAll))
            return Results.Forbid();
        if (!TryDateRange(from, to, out var startUtc, out var endUtc))
            return Results.BadRequest(new { message = "Indique un rango válido de fechas (AAAA-MM-DD)." });
        if (search is { Length: > 200 })
            return Results.BadRequest(new { message = "La búsqueda no puede exceder 200 caracteres." });

        return Results.Ok(await ReadAsync(db, catalog, startUtc, endUtc, search, ct));
    }

    private static async Task<IResult> ExportAsync(
        string? from, string? to, string? search,
        ServiceDbContext db,
        IPricingConfigCatalogClient catalog,
        IPricingReportsClient reports,
        SellerVisibilityService visibility,
        HttpContext context,
        CancellationToken ct)
    {
        if (!visibility.HasScope(context.User, PricingConstants.Scopes.RateRequestViewAll))
            return Results.Forbid();
        if (!TryDateRange(from, to, out var startUtc, out var endUtc))
            return Results.BadRequest(new { message = "Indique un rango válido de fechas (AAAA-MM-DD)." });
        if (search is { Length: > 200 })
            return Results.BadRequest(new { message = "La búsqueda no puede exceder 200 caracteres." });

        var rows = await ReadAsync(db, catalog, startUtc, endUtc, search, ct);
        // QUO and IDTRA must remain text, including comma-separated IDTRAs and leading zeros.
        // Numbers are serialized as JSON numbers, not strings, so Excel can sum them.
        var data = rows.Select(row => new Dictionary<string, object?>
        {
            ["Fecha"] = row.CreatedAtUtc.AddHours(-6).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["QUO"] = row.Quo,
            ["Cliente"] = row.Client,
            ["Detalle de tarifa"] = row.Details,
            ["IDTRA"] = row.Idtra,
            ["Moneda"] = row.Currency,
            ["Venta"] = row.Sale,
            ["Costo"] = row.Cost
        }).ToList();
        var report = await reports.GenerateTabularAsync(
            "xlsx", JsonSerializer.Serialize(data),
            $"reporte-tarifas-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            "Tarifas", ct);
        return Results.File(report.Content, SpreadsheetMime, report.FileName);
    }

    private static bool TryDateRange(
        string? from, string? to, out DateTime? startUtc, out DateTime? endUtc)
    {
        startUtc = null;
        endUtc = null;
        if (!TryDate(from, out var first) || !TryDate(to, out var last))
            return false;
        if (first is { } f && last is { } l && f > l)
            return false;

        // Costa Rica observes UTC-06:00 all year; upper bound is exclusive.
        if (first is { } beginning)
            startUtc = DateTime.SpecifyKind(beginning, DateTimeKind.Utc).AddHours(6);
        if (last is { } ending)
            endUtc = DateTime.SpecifyKind(ending, DateTimeKind.Utc).AddDays(1).AddHours(6);
        return true;
    }

    private static bool TryDate(string? text, out DateTime? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) || parsed.Year == 9999)
            return false;
        date = parsed;
        return true;
    }

    private static async Task<List<BasicRateRow>> ReadAsync(
        ServiceDbContext db, IPricingConfigCatalogClient catalog,
        DateTime? startUtc, DateTime? endUtc, string? search, CancellationToken ct)
    {
        // Do not mix master tariffs with client quotations.
        var query = db.RateHeaders.AsNoTracking()
            .Where(rate => !rate.IsDeleted && rate.RateType != RateType.Tariff);

        if (startUtc.HasValue)
            query = query.Where(rate => rate.CreatedAtUtc >= startUtc.Value);
        if (endUtc.HasValue)
            query = query.Where(rate => rate.CreatedAtUtc < endUtc.Value);

        var term = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
            query = query.Where(rate =>
                rate.RateCode.ToLower().Contains(term) ||
                (rate.QuoNumber != null && rate.QuoNumber.ToLower().Contains(term)) ||
                (rate.ClientName != null && rate.ClientName.ToLower().Contains(term)) ||
                (rate.IdtraNumber != null && rate.IdtraNumber.ToLower().Contains(term)) ||
                rate.PolName.ToLower().Contains(term) ||
                rate.PoeName.ToLower().Contains(term) ||
                (rate.PodName != null && rate.PodName.ToLower().Contains(term)));

        var raw = await query
            .OrderByDescending(rate => rate.CreatedAtUtc)
            .ThenByDescending(rate => rate.RateCode)
            .Select(rate => new
            {
                rate.Id, rate.RateCode, rate.QuoNumber, rate.ClientName, rate.IdtraNumber,
                rate.ShipmentMode, rate.PolName, rate.PoeName, rate.PodName,
                rate.ContainerTypeName, rate.ContainerQuantity, rate.CurrencyId,
                rate.CurrencyCode, rate.CurrencyName, rate.TotalSaleAmount,
                rate.TotalCostAmount, rate.CreatedAtUtc
            })
            .ToListAsync(ct);

        var currencies = (await catalog.GetActiveByGroupAsync(
                PricingConstants.CatalogSlugs.Currencies, ct))
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().Value!.Trim());

        return raw.Select(rate =>
        {
            var route = new[] { rate.PolName, rate.PoeName, rate.PodName }
                .Where(point => !string.IsNullOrWhiteSpace(point))
                .Aggregate(new List<string>(), (points, point) =>
                {
                    if (points.Count == 0 || !string.Equals(points[^1], point, StringComparison.OrdinalIgnoreCase))
                        points.Add(point!);
                    return points;
                });
            var transport = rate.ShipmentMode.ToString();
            var equipment = rate.ContainerQuantity > 0 && !string.IsNullOrWhiteSpace(rate.ContainerTypeName)
                ? $"{rate.ContainerQuantity} × {rate.ContainerTypeName}"
                : "";
            var details = string.Join(" · ", new[]
            {
                transport,
                string.Join(" → ", route),
                equipment
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

            return new BasicRateRow(
                rate.Id,
                string.IsNullOrWhiteSpace(rate.QuoNumber) ? rate.RateCode : rate.QuoNumber,
                rate.ClientName ?? "",
                details,
                rate.IdtraNumber ?? "",
                currencies.TryGetValue(rate.CurrencyId, out var currency)
                    ? currency
                    : NormalizeCurrency(rate.CurrencyName, rate.CurrencyCode),
                rate.TotalSaleAmount,
                rate.TotalCostAmount,
                rate.CreatedAtUtc);
        }).ToList();
    }

    private static string NormalizeCurrency(string? name, string? code)
    {
        foreach (var candidate in new[] { name, code })
        {
            var currency = candidate?.Trim().ToUpperInvariant();
            if (currency is "USD" or "CRC" or "EUR") return currency;
            if (currency is not null && (currency.Contains("DOLAR") || currency.Contains("DÓLAR") || currency.Contains("DOLLAR"))) return "USD";
            if (currency is not null && (currency.Contains("COLON") || currency.Contains("COLÓN"))) return "CRC";
        }
        return "SIN MONEDA";
    }

    private sealed record BasicRateRow(
        Guid Id, string Quo, string Client, string Details, string Idtra,
        string Currency, decimal Sale, decimal Cost, DateTime CreatedAtUtc);
}

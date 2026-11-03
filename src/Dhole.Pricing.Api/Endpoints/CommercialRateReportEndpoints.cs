using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Read-only, system-wide commercial quotation report. Unlike rate-request reports,
/// this includes quotations without a linked rate request.
/// </summary>
public static class CommercialRateReportEndpoints
{
    // Costa Rica no utiliza horario de verano. El cierre comercial mensual se
    // calcula en hora local, no en el cambio de día UTC.
    private static readonly TimeZoneInfo BusinessZone =
        TimeZoneInfo.CreateCustomTimeZone(
            "Costa_Rica_UTC-6", TimeSpan.FromHours(-6),
            "Costa Rica (UTC-6)", "Costa Rica");

    private static DateTime StartOfBusinessDayUtc(DateTime localDate) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified), BusinessZone);

    public static IEndpointRouteBuilder MapCommercialRateReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rates")
            .WithTags("Commercial quotations")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestViewAll);

        group.MapGet("/commercial-report", GetAsync);
        group.MapGet("/commercial-report.xlsx", ExportAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(
        string? status,
        string? search,
        string? month,
        string? seller,
        string? fromDate,
        string? toDate,
        string? dateType,
        ServiceDbContext db,
        IPricingConfigCatalogClient configCatalog,
        SellerVisibilityService visibility,
        HttpContext context,
        CancellationToken ct)
    {
        if (!visibility.HasScope(context.User, PricingConstants.Scopes.RateRequestViewAll))
            return Results.Forbid();

        if (!ValidStatus(status))
            return Results.BadRequest(new { message = "El estado indicado no es válido." });

        if (!TryParseMonth(month, out var monthStart))
            return Results.BadRequest(new { message = "El mes debe tener el formato AAAA-MM." });

        if (!TryParseDate(fromDate, out var from))
            return Results.BadRequest(new { message = "La fecha inicial debe tener el formato AAAA-MM-DD." });

        if (!TryParseDate(toDate, out var to))
            return Results.BadRequest(new { message = "La fecha final debe tener el formato AAAA-MM-DD." });

        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return Results.BadRequest(new { message = "La fecha inicial no puede ser posterior a la fecha final." });

        if (!ValidDateType(dateType))
            return Results.BadRequest(new { message = "El tipo de fecha debe ser created o approved." });

        return Results.Ok(await ReadRowsAsync(db, configCatalog, status, search, seller, monthStart, from, to, dateType, ct));
    }

    private static async Task<IResult> ExportAsync(
        string? status,
        string? search,
        string? month,
        string? seller,
        string? fromDate,
        string? toDate,
        string? dateType,
        ServiceDbContext db,
        IPricingConfigCatalogClient configCatalog,
        SellerVisibilityService visibility,
        HttpContext context,
        CancellationToken ct)
    {
        if (!visibility.HasScope(context.User, PricingConstants.Scopes.RateRequestViewAll))
            return Results.Forbid();

        if (!ValidStatus(status))
            return Results.BadRequest(new { message = "El estado indicado no es válido." });

        if (!TryParseMonth(month, out var monthStart))
            return Results.BadRequest(new { message = "El mes debe tener el formato AAAA-MM." });

        if (!TryParseDate(fromDate, out var from))
            return Results.BadRequest(new { message = "La fecha inicial debe tener el formato AAAA-MM-DD." });

        if (!TryParseDate(toDate, out var to))
            return Results.BadRequest(new { message = "La fecha final debe tener el formato AAAA-MM-DD." });

        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return Results.BadRequest(new { message = "La fecha inicial no puede ser posterior a la fecha final." });

        if (!ValidDateType(dateType))
            return Results.BadRequest(new { message = "El tipo de fecha debe ser created o approved." });

        var rows = await ReadRowsAsync(db, configCatalog, status, search, seller, monthStart, from, to, dateType, ct);
        return Results.File(
            BuildWorkbook(rows),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"reporte-cotizaciones-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    private static bool ValidStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Equals("Sent", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("AcceptedByClient", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("RejectedByClient", StringComparison.OrdinalIgnoreCase);

    private static bool ValidDateType(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Equals("created", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("approved", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseMonth(string? month, out DateTime? monthStart)
    {
        monthStart = null;
        if (string.IsNullOrWhiteSpace(month)) return true;

        if (month.Length != 7 ||
            !DateTime.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ||
            (date.Year == 9999 && date.Month == 12))
            return false;

        monthStart = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        return true;
    }

    private static bool TryParseDate(string? value, out DateTime? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (value.Length != 10 ||
            !DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;

        date = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return true;
    }

    private static async Task<List<CommercialQuote>> ReadRowsAsync(
        ServiceDbContext db, IPricingConfigCatalogClient configCatalog,
        string? status, string? search, string? seller, DateTime? monthStart,
        DateTime? fromDate, DateTime? toDate, string? dateType, CancellationToken ct)
    {
        var query = db.RateHeaders.AsNoTracking()
            .Where(rate => !rate.IsDeleted &&
                !(rate.RateType == RateType.Tariff &&
                  rate.ClientName != null && rate.ClientName.ToLower().Contains("tarifario")))
            .Where(rate =>
                rate.Status == RateStatus.Sent ||
                rate.Status == RateStatus.RequestedByClient ||
                rate.Status == RateStatus.AcceptedByClient ||
                rate.Status == RateStatus.RejectedByClient ||
                rate.Status == RateStatus.Closed);

        if (status?.Equals("Sent", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(rate => rate.Status == RateStatus.Sent || rate.Status == RateStatus.RequestedByClient);
        else if (status?.Equals("AcceptedByClient", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(rate => rate.Status == RateStatus.AcceptedByClient);
        else if (status?.Equals("RejectedByClient", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(rate => rate.Status == RateStatus.RejectedByClient || rate.Status == RateStatus.Closed);

        var filterByApprovedDate = dateType?.Equals("approved", StringComparison.OrdinalIgnoreCase) == true;

        // Filtros por creación cuando se selecciona esa fecha. El modo aprobación se
        // resuelve después de recuperar el historial de eventos de las tarifas antiguas.
        if (!filterByApprovedDate && monthStart is { } from)
        {
            var utcFrom = StartOfBusinessDayUtc(from);
            var until = StartOfBusinessDayUtc(from.AddMonths(1));
            query = query.Where(rate => rate.CreatedAtUtc >= utcFrom && rate.CreatedAtUtc < until);
        }

        // Límites de fechas locales de Costa Rica convertidos a UTC para la consulta.
        if (!filterByApprovedDate && fromDate is { } fromDateLocal)
        {
            var fromUtc = StartOfBusinessDayUtc(fromDateLocal);
            query = query.Where(rate => rate.CreatedAtUtc >= fromUtc);
        }

        if (!filterByApprovedDate && toDate is { } toDateLocal)
        {
            var untilExclusive = toDateLocal == DateTime.MaxValue.Date
                ? DateTime.MaxValue
                : StartOfBusinessDayUtc(toDateLocal.AddDays(1));
            query = query.Where(rate => rate.CreatedAtUtc < untilExclusive);
        }

        if (!string.IsNullOrWhiteSpace(seller))
        {
            var sellerName = seller.Trim().ToLowerInvariant();
            if (sellerName == "__unassigned__")
                query = query.Where(rate => rate.ExecutiveName == null || rate.ExecutiveName.Trim() == "");
            else
                query = query.Where(rate =>
                    rate.ExecutiveName != null && rate.ExecutiveName.Trim().ToLower() == sellerName);
        }

        var needle = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(needle))
            query = query.Where(rate =>
                rate.RateCode.ToLower().Contains(needle) ||
                (rate.QuoNumber != null && rate.QuoNumber.ToLower().Contains(needle)) ||
                (rate.IdtraNumber != null && rate.IdtraNumber.ToLower().Contains(needle)) ||
                (rate.ClientName != null && rate.ClientName.ToLower().Contains(needle)) ||
                (rate.ExecutiveName != null && rate.ExecutiveName.ToLower().Contains(needle)) ||
                rate.PolName.ToLower().Contains(needle) ||
                rate.PoeName.ToLower().Contains(needle) ||
                (rate.PodName != null && rate.PodName.ToLower().Contains(needle)) ||
                rate.ContainerTypeName.ToLower().Contains(needle));

        var data = await query
            .OrderByDescending(rate => rate.CreatedAtUtc)
            .Select(rate => new
            {
                rate.Id,
                rate.RateCode,
                rate.QuoNumber,
                rate.IdtraNumber,
                rate.ClientName,
                rate.ExecutiveName,
                rate.ShipmentMode,
                rate.PolName,
                rate.PoeName,
                rate.PodName,
                rate.ContainerTypeName,
                rate.ContainerQuantity,
                rate.CurrencyId,
                rate.CurrencyName,
                rate.CurrencyCode,
                rate.TotalSaleAmount,
                rate.TotalCostAmount,
                rate.TotalUtilityAmount,
                rate.MarginPercentage,
                rate.CreatedAtUtc,
                rate.AcceptedAtUtc,
                rate.ValidFrom,
                rate.ValidTo,
                rate.ClosedAtUtc,
                rate.ClosedReason,
                rate.Status,
            })
            .ToListAsync(ct);

        // Moneda de negocio: Config CatalogItem.Value, no el identificador técnico Code.
        // Consultar el catálogo una sola vez para todo el reporte evita búsquedas por fila.
        var currencyItems = await configCatalog.GetActiveByGroupAsync(
            PricingConstants.CatalogSlugs.Currencies, ct);
        var currencyValues = currencyItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().Value!.Trim());

        // Las tarifas aceptadas antes de agregar AcceptedAtUtc no deben recibir
        // una fecha inventada (p. ej. UpdatedAtUtc). Reutilizar solo eventos reales
        // de aceptación registrados en el outbox de auditoría.
        var legacyDates = new Dictionary<Guid, DateTime>();
        var missingDates = data
            .Where(rate => rate.Status == RateStatus.AcceptedByClient && !rate.AcceptedAtUtc.HasValue)
            .Select(rate => rate.Id)
            .ToHashSet();
        if (missingDates.Count > 0)
        {
            var events = await db.OutboxMessages.AsNoTracking()
                .Where(message => message.EventName == "pricing.rate.accepted_by_client")
                .Select(message => message.PayloadJson)
                .ToListAsync(ct);
            foreach (var payload in events)
            {
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("rateId", out var rateValue) ||
                        !Guid.TryParse(rateValue.GetString(), out var rateId) ||
                        !missingDates.Contains(rateId) ||
                        !root.TryGetProperty("occurredAt", out var timeValue) ||
                        !timeValue.TryGetDateTime(out var occurredAt))
                        continue;
                    var approvedAt = occurredAt.Kind == DateTimeKind.Utc
                        ? occurredAt : occurredAt.ToUniversalTime();
                    if (!legacyDates.TryGetValue(rateId, out var previous) || approvedAt > previous)
                        legacyDates[rateId] = approvedAt;
                }
                catch (JsonException)
                {
                    // Un evento antiguo inválido no debe impedir consultar el reporte.
                }
            }
        }

        var result = data.Select(rate => new CommercialQuote(
            rate.Id,
            rate.RateCode,
            rate.QuoNumber,
            rate.IdtraNumber,
            rate.ClientName,
            rate.ExecutiveName,
            rate.ShipmentMode.ToString(),
            rate.PolName,
            rate.PoeName,
            rate.PodName,
            rate.ContainerTypeName,
            rate.ContainerQuantity,
            currencyValues.TryGetValue(rate.CurrencyId, out var catalogValue)
                ? catalogValue
                : LegacyCurrencyValue(rate.CurrencyName, rate.CurrencyCode),
            rate.TotalSaleAmount,
            rate.TotalCostAmount,
            rate.TotalUtilityAmount,
            rate.MarginPercentage,
            rate.CreatedAtUtc,
            rate.AcceptedAtUtc ?? (legacyDates.TryGetValue(rate.Id, out var historicalDate)
                ? historicalDate : (DateTime?)null),
            rate.ValidFrom,
            rate.ValidTo,
            rate.ClosedAtUtc,
            rate.ClosedReason,
            rate.Status == RateStatus.AcceptedByClient ? "AcceptedByClient" :
            rate.Status is RateStatus.RejectedByClient or RateStatus.Closed ? "RejectedByClient" : "Sent"
        )).ToList();

        if (!filterByApprovedDate ||
            (!monthStart.HasValue && !fromDate.HasValue && !toDate.HasValue))
            return result;

        var monthFromUtc = monthStart.HasValue ? StartOfBusinessDayUtc(monthStart.Value) : (DateTime?)null;
        var monthUntilUtc = monthStart.HasValue ? StartOfBusinessDayUtc(monthStart.Value.AddMonths(1)) : (DateTime?)null;
        var fromUtc = fromDate.HasValue ? StartOfBusinessDayUtc(fromDate.Value) : (DateTime?)null;
        var untilUtc = toDate.HasValue
            ? (toDate.Value == DateTime.MaxValue.Date
                ? DateTime.MaxValue
                : StartOfBusinessDayUtc(toDate.Value.AddDays(1)))
            : (DateTime?)null;

        return result.Where(rate =>
            rate.Status == "AcceptedByClient" &&
            rate.AcceptedAtUtc is { } acceptedAt &&
            (!monthFromUtc.HasValue || acceptedAt >= monthFromUtc.Value) &&
            (!monthUntilUtc.HasValue || acceptedAt < monthUntilUtc.Value) &&
            (!fromUtc.HasValue || acceptedAt >= fromUtc.Value) &&
            (!untilUtc.HasValue || acceptedAt < untilUtc.Value))
            .ToList();
    }

    private sealed record CommercialQuote(
        Guid Id,
        string RateCode,
        string? QuoNumber,
        string? IdtraNumber,
        string? ClientName,
        string? ExecutiveName,
        string ShipmentMode,
        string PolName,
        string PoeName,
        string? PodName,
        string ContainerTypeName,
        int ContainerQuantity,
        string CurrencyValue,
        decimal TotalSaleAmount,
        decimal TotalCostAmount,
        decimal TotalUtilityAmount,
        decimal MarginPercentage,
        DateTime CreatedAtUtc,
        DateTime? AcceptedAtUtc,
        DateTime ValidFrom,
        DateTime ValidTo,
        DateTime? ClosedAtUtc,
        string? ClosedReason,
        string Status);

    private static string LegacyCurrencyValue(string? name, string? code)
    {
        // Solo compatibilidad con cotizaciones antiguas cuyo catálogo fue desactivado.
        // Nunca exponer identificadores sintéticos del tipo CUR-2026-001.
        foreach (var candidate in new[] { name, code })
        {
            var normalized = candidate?.Trim().ToUpperInvariant();
            if (normalized is "USD" or "CRC" or "EUR") return normalized;
            if (normalized is not null && (normalized.Contains("DÓLAR") || normalized.Contains("DOLAR") || normalized.Contains("DOLLAR"))) return "USD";
            if (normalized is not null && (normalized.Contains("COLÓN") || normalized.Contains("COLON"))) return "CRC";
        }

        return "SIN MONEDA";
    }

    private static string BusinessDate(DateTime? utc) => utc is { } value
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), BusinessZone)
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        : string.Empty;

    private static byte[] BuildWorkbook(IReadOnlyList<CommercialQuote> rates)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Add(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Add(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Reporte comercial" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Add(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            AppendRow(xml, 1, new[]
            {
                "Código", "Cotización", "IDTRA", "Cliente", "Vendedor", "Modalidad",
                "POL", "POE", "POD", "Equipo", "Cantidad", "Moneda", "Venta",
                "Costo", "Utilidad", "Margen (%)", "Estado", "Fecha creada (CR)",
                "Fecha aprobación (CR)", "Vigente desde", "Vigente hasta", "Fecha cierre", "Motivo cierre"
            }, false);

            var rowNumber = 2;
            foreach (var rate in rates)
            {
                AppendRow(xml, rowNumber++, new[]
                {
                    rate.RateCode, rate.QuoNumber ?? "", rate.IdtraNumber ?? "", rate.ClientName ?? "",
                    rate.ExecutiveName ?? "", rate.ShipmentMode, rate.PolName, rate.PoeName,
                    rate.PodName ?? "", rate.ContainerTypeName,
                    rate.ContainerQuantity.ToString(CultureInfo.InvariantCulture), rate.CurrencyValue,
                    rate.TotalSaleAmount.ToString(CultureInfo.InvariantCulture),
                    rate.TotalCostAmount.ToString(CultureInfo.InvariantCulture),
                    rate.TotalUtilityAmount.ToString(CultureInfo.InvariantCulture),
                    rate.MarginPercentage.ToString(CultureInfo.InvariantCulture),
                    rate.Status == "Sent" ? "Enviada" : rate.Status == "AcceptedByClient" ? "Aceptada" : "No aceptada",
                    BusinessDate(rate.CreatedAtUtc),
                    BusinessDate(rate.AcceptedAtUtc),
                    rate.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    rate.ValidTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    rate.ClosedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                    rate.ClosedReason ?? ""
                }, true);
            }
            xml.Append("</sheetData></worksheet>");
            Add(archive, "xl/worksheets/sheet1.xml", xml.ToString());
        }

        return stream.ToArray();
    }

    private static void AppendRow(StringBuilder xml, int rowNumber, IReadOnlyList<string> values, bool numbers)
    {
        xml.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (var column = 0; column < values.Count; column++)
        {
            var cell = ColumnName(column + 1) + rowNumber;
            if (numbers && (column is 10 or 12 or 13 or 14 or 15))
                xml.Append("<c r=\"").Append(cell).Append("\"><v>").Append(values[column]).Append("</v></c>");
            else
                xml.Append("<c r=\"").Append(cell).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                    .Append(System.Security.SecurityElement.Escape(values[column]))
                    .Append("</t></is></c>");
        }
        xml.Append("</row>");
    }

    private static string ColumnName(int number)
    {
        var name = string.Empty;
        for (; number > 0; number /= 26)
            name = (char)('A' + (number - 1) % 26) + name;
        return name;
    }

    private static void Add(ZipArchive archive, string path, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(value.Trim());
    }
}

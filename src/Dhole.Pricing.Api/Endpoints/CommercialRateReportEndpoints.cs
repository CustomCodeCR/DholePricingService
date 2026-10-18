using System.Globalization;
using System.IO.Compression;
using System.Text;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Services;
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
    public static IEndpointRouteBuilder MapCommercialRateReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rates")
            .WithTags("Commercial quotations")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateView);

        group.MapGet("/commercial-report", GetAsync);
        group.MapGet("/commercial-report.xlsx", ExportAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(
        string? status,
        string? search,
        ServiceDbContext db,
        SellerVisibilityService visibility,
        HttpContext context,
        CancellationToken ct)
    {
        if (!visibility.HasScope(context.User, PricingConstants.Scopes.RateRequestViewAll))
            return Results.Forbid();

        if (!ValidStatus(status))
            return Results.BadRequest(new { message = "El estado indicado no es válido." });

        return Results.Ok(await ReadRowsAsync(db, status, search, ct));
    }

    private static async Task<IResult> ExportAsync(
        string? status,
        string? search,
        ServiceDbContext db,
        SellerVisibilityService visibility,
        HttpContext context,
        CancellationToken ct)
    {
        if (!visibility.HasScope(context.User, PricingConstants.Scopes.RateRequestViewAll))
            return Results.Forbid();

        if (!ValidStatus(status))
            return Results.BadRequest(new { message = "El estado indicado no es válido." });

        var rows = await ReadRowsAsync(db, status, search, ct);
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

    private static async Task<List<CommercialQuote>> ReadRowsAsync(
        ServiceDbContext db, string? status, string? search, CancellationToken ct)
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
                rate.CurrencyCode,
                rate.TotalSaleAmount,
                rate.TotalCostAmount,
                rate.TotalUtilityAmount,
                rate.MarginPercentage,
                rate.CreatedAtUtc,
                rate.ValidFrom,
                rate.ValidTo,
                rate.ClosedAtUtc,
                rate.ClosedReason,
                rate.Status,
            })
            .ToListAsync(ct);

        return data.Select(rate => new CommercialQuote(
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
            rate.CurrencyCode,
            rate.TotalSaleAmount,
            rate.TotalCostAmount,
            rate.TotalUtilityAmount,
            rate.MarginPercentage,
            rate.CreatedAtUtc,
            rate.ValidFrom,
            rate.ValidTo,
            rate.ClosedAtUtc,
            rate.ClosedReason,
            rate.Status == RateStatus.AcceptedByClient ? "AcceptedByClient" :
            rate.Status is RateStatus.RejectedByClient or RateStatus.Closed ? "RejectedByClient" : "Sent"
        )).ToList();
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
        string CurrencyCode,
        decimal TotalSaleAmount,
        decimal TotalCostAmount,
        decimal TotalUtilityAmount,
        decimal MarginPercentage,
        DateTime CreatedAtUtc,
        DateTime ValidFrom,
        DateTime ValidTo,
        DateTime? ClosedAtUtc,
        string? ClosedReason,
        string Status);

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
                "Costo", "Utilidad", "Margen (%)", "Estado", "Fecha creada",
                "Vigente desde", "Vigente hasta", "Fecha cierre", "Motivo cierre"
            }, false);

            var rowNumber = 2;
            foreach (var rate in rates)
            {
                AppendRow(xml, rowNumber++, new[]
                {
                    rate.RateCode, rate.QuoNumber ?? "", rate.IdtraNumber ?? "", rate.ClientName ?? "",
                    rate.ExecutiveName ?? "", rate.ShipmentMode, rate.PolName, rate.PoeName,
                    rate.PodName ?? "", rate.ContainerTypeName,
                    rate.ContainerQuantity.ToString(CultureInfo.InvariantCulture), rate.CurrencyCode,
                    rate.TotalSaleAmount.ToString(CultureInfo.InvariantCulture),
                    rate.TotalCostAmount.ToString(CultureInfo.InvariantCulture),
                    rate.TotalUtilityAmount.ToString(CultureInfo.InvariantCulture),
                    rate.MarginPercentage.ToString(CultureInfo.InvariantCulture),
                    rate.Status == "Sent" ? "Enviada" : rate.Status == "AcceptedByClient" ? "Aceptada" : "No aceptada",
                    rate.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
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

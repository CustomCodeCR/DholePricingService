using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateRequestReportingEndpoints
{
    private const string ViewAllScope = "pricing.rate-request.view-all";

    public static IEndpointRouteBuilder MapRateRequestReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rate-requests")
            .WithTags("Rate requests")
            .RequireAuthorization();

        group.MapGet("/all", GetAllAsync).RequireScope(ViewAllScope);
        group.MapGet("/export.xlsx", ExportExcelAsync).RequireScope(ViewAllScope);
        return app;
    }

    private static async Task<IResult> GetAllAsync(ServiceDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.RateRequests
            .AsNoTracking()
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => new
            {
                x.Id,
                status = x.Status.ToString(),
                priority = x.Priority.ToString(),
                x.RequestedAtUtc,
                x.DueAtUtc,
                x.CompletedAtUtc,
                x.RateId,
                x.SellerUserId,
                x.SellerName,
                x.SellerEmail,
                x.ClientName,
                x.ExecutiveName,
                x.ShipmentMode,
                x.OriginName,
                x.DestinationName,
                x.PoeId,
                x.PoeName,
                x.PodId,
                x.PodName,
                x.PayloadJson,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(rows.Select(x => new
        {
            x.Id,
            x.status,
            x.priority,
            x.RequestedAtUtc,
            x.DueAtUtc,
            x.CompletedAtUtc,
            x.RateId,
            x.SellerUserId,
            x.SellerName,
            x.SellerEmail,
            x.ClientName,
            x.ExecutiveName,
            x.ShipmentMode,
            x.OriginName,
            x.DestinationName,
            x.PoeId,
            x.PoeName,
            x.PodId,
            x.PodName,
            payload = ParsePayload(x.PayloadJson),
        }));
    }

    private static async Task<IResult> ExportExcelAsync(ServiceDbContext db, CancellationToken cancellationToken)
    {
        var requests = await db.RateRequests
            .AsNoTracking()
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(cancellationToken);

        var content = BuildWorkbook(requests);
        var fileName = $"tarifas-solicitadas-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx";
        return Results.File(
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static byte[] BuildWorkbook(IReadOnlyList<RateRequest> requests)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Tarifas solicitadas" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            var headers = new[]
            {
                "Solicitud", "Estado", "Prioridad", "Fecha solicitud", "Vence SLA", "Fecha completada",
                "Vendedor", "Correo vendedor", "Cliente", "Ejecutivo", "Modalidad", "Origen", "POE", "POD",
                "Tarifa asociada", "Fecha de carga", "Contenedor/Equipo", "Cantidad", "Incoterm", "Servicios"
            };

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            AppendRow(xml, 1, headers);

            var rowNumber = 2;
            foreach (var request in requests)
            {
                var payload = ExtractPayloadFields(request.PayloadJson);
                AppendRow(xml, rowNumber++, new[]
                {
                    request.Id.ToString(), request.Status.ToString(), request.Priority.ToString(),
                    FormatDate(request.RequestedAtUtc), FormatDate(request.DueAtUtc), FormatDate(request.CompletedAtUtc),
                    request.SellerName ?? "", request.SellerEmail ?? "", request.ClientName ?? "", request.ExecutiveName ?? "",
                    request.ShipmentMode ?? "", request.OriginName ?? "", request.PoeName ?? "", request.PodName ?? request.DestinationName ?? "",
                    request.RateId?.ToString() ?? "", payload.LoadDate, payload.Equipment, payload.Quantity,
                    payload.Incoterm, payload.Services
                });
            }

            xml.Append("</sheetData></worksheet>");
            WriteEntry(archive, "xl/worksheets/sheet1.xml", xml.ToString());
        }
        return stream.ToArray();
    }

    private static void AppendRow(StringBuilder xml, int rowNumber, IReadOnlyList<string> values)
    {
        xml.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (var index = 0; index < values.Count; index++)
        {
            var reference = ColumnName(index + 1) + rowNumber;
            xml.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(Escape(values[index]))
                .Append("</t></is></c>");
        }
        xml.Append("</row>");
    }

    private static string ColumnName(int index)
    {
        var value = string.Empty;
        while (index > 0)
        {
            index--;
            value = (char)('A' + index % 26) + value;
            index /= 26;
        }
        return value;
    }

    private static string Escape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string FormatDate(DateTime? value)
        => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm 'UTC'") : string.Empty;

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static JsonElement ParsePayload(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
    }

    private static (string LoadDate, string Equipment, string Quantity, string Incoterm, string Services) ExtractPayloadFields(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var form = document.RootElement;
            if (TryGet(form, "form", out var nested) && nested.ValueKind == JsonValueKind.Object) form = nested;

            var loadDate = GetText(form, "loadDate") ?? GetText(form, "cargoDate") ?? GetText(form, "fechaCarga") ?? "";
            var equipment = GetText(form, "equipmentType") ?? GetText(form, "containerType") ?? GetText(form, "truckType") ?? "";
            var quantity = GetText(form, "quantity") ?? GetText(form, "containerQuantity") ?? GetText(form, "truckQuantity") ?? "";
            var incoterm = GetText(form, "incoterm") ?? GetText(form, "incotermName") ?? "";
            var services = GetText(form, "services") ?? GetArrayText(form, "services") ?? "";
            return (loadDate, equipment, quantity, incoterm, services);
        }
        catch (JsonException)
        {
            return ("", "", "", "", "");
        }
    }

    private static string? GetArrayText(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind != JsonValueKind.Array) return null;
        return string.Join(", ", value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()));
    }

    private static string? GetText(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Microsoft.Extensions.Configuration;
using QRCoder;

namespace Dhole.Pricing.Infrastructure.Reports;

public sealed class RateReportDataFactory(IConfiguration configuration) : IRateReportDataFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("en-US");
    private const string OriginOfficeMessage = "Estos son los datos de Castro Fallas en origen.";

    public string CreateDataJson(RateHeader rate)
    {
        string Text(string? value, string fallback = "No especificado") =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        string CurrencyValue(string? name, string? code) =>
            Text(name, Text(code, "USD"));

        var currencyValue = CurrencyValue(rate.CurrencyName, rate.CurrencyCode);
        string Money(decimal amount) => $"{currencyValue} {amount.ToString("N2", MoneyCulture)}";
        string DetailMoney(RateDetail detail, decimal amount) =>
            $"{CurrencyValue(detail.CurrencyName, detail.CurrencyCode)} {amount.ToString("N2", MoneyCulture)}";

        var commercialTerms = ExclusiveCommercialTerms(rate.Includes, rate.SubjectTo, rate.Excludes);
        var originOfficePublicUrl = CreateOriginOfficePublicUrl(rate);
        var originOfficeQrDataUri = CreateQrDataUri(originOfficePublicUrl);
        var showCarrier = rate.ShipmentMode != ShipmentMode.Lcl;

        var containers = (rate.RateContainers.Count > 0
                ? rate.RateContainers
                    .OrderBy(x => x.ContainerTypeName)
                    .ThenBy(x => x.ContainerTypeCode)
                    .Select(x => new
                    {
                        containerTypeId = x.ContainerTypeId,
                        containerType = x.ContainerTypeName,
                        containerTypeName = x.ContainerTypeName,
                        containerTypeCode = x.ContainerTypeCode,
                        quantity = x.Quantity,
                        label = $"{x.Quantity} x {x.ContainerTypeName}"
                    })
                : new[]
                {
                    new
                    {
                        containerTypeId = rate.ContainerTypeId,
                        containerType = rate.ContainerTypeName,
                        containerTypeName = rate.ContainerTypeName,
                        containerTypeCode = rate.ContainerTypeCode,
                        quantity = rate.ContainerQuantity,
                        label = $"{rate.ContainerQuantity} x {rate.ContainerTypeName}"
                    }
                })
            .ToArray();
        var equipmentSummary = string.Join(" + ", containers.Select(x => x.label));
        var shipmentSummary = rate.ShipmentMode switch
        {
            ShipmentMode.Lcl => $"LCL · {rate.ChargeableQuantity.ToString("N3", MoneyCulture)} CBM cobrable",
            ShipmentMode.Ltl => $"LTL · {rate.ChargeableQuantity.ToString("N3", MoneyCulture)} CBM cobrable",
            ShipmentMode.Ftl => $"FTL · {rate.ContainerQuantity} camión{(rate.ContainerQuantity == 1 ? string.Empty : "es")}",
            _ => equipmentSummary,
        };

        // Un consolidado LCL propio toma sus líneas comerciales exclusivamente de la matriz
        // Excel (EXW/FCA/FOB). Los CostId pertenecen al catálogo general "Costos y recargos"
        // y no deben aparecer ni alterar el PDF, incluso en tarifas antiguas que los guardaron.
        var ownLclExcelOnly = rate.ShipmentMode == ShipmentMode.Lcl
            && (
                string.Equals(rate.AgentCode, "GCF", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rate.AgentName, "Grupo Castro Fallas", StringComparison.OrdinalIgnoreCase)
                || rate.RateDetails.Any(detail =>
                    !detail.CostId.HasValue
                    && (
                        (detail.Notes?.Contains("LCL PROPIO", StringComparison.OrdinalIgnoreCase) ?? false)
                        || (detail.Notes?.Contains("Base del Excel", StringComparison.OrdinalIgnoreCase) ?? false)
                        || detail.Name.Contains("LCL PROPIO", StringComparison.OrdinalIgnoreCase)
                    )
                )
            );

        var reportDetails = rate.RateDetails
            .Where(detail => !ownLclExcelOnly || !detail.CostId.HasValue)
            .Where(detail => detail.SaleAmount * detail.Quantity != 0m)
            .OrderBy(x => x.CostDetailType)
            .ThenBy(x => x.Name)
            .ToArray();

        var items = reportDetails
            .Select(detail => new
            {
                description = detail.Name,
                quantity = detail.Quantity,
                currency = CurrencyValue(detail.CurrencyName, detail.CurrencyCode),
                currencyCode = detail.CurrencyCode,
                unitSale = DetailMoney(detail, detail.SaleAmount),
                unitSaleAmount = detail.SaleAmount,
                lineTotal = DetailMoney(detail, detail.SaleAmount * detail.Quantity),
                lineTotalAmount = detail.SaleAmount * detail.Quantity,
                notes = detail.CostDetailType == CostDetailType.Insurance
                    ? string.Empty
                    : Text(detail.Notes, string.Empty)
            })
            .ToArray();

        var rows = reportDetails
            .Select(detail => new Dictionary<string, object?>
            {
                ["Concepto"] = detail.Name,
                ["Cantidad"] = detail.Quantity,
                ["Moneda"] = CurrencyValue(detail.CurrencyName, detail.CurrencyCode),
                ["Precio unitario"] = detail.SaleAmount,
                ["Total"] = detail.SaleAmount * detail.Quantity,
                ["Notas"] = detail.CostDetailType == CostDetailType.Insurance
                    ? string.Empty
                    : detail.Notes
            })
            .ToArray();

        var data = new
        {
            company = new
            {
                name = configuration["Reports:Company:Name"] ?? "Grupo Castro Fallas",
                legalName = configuration["Reports:Company:LegalName"] ?? "Grupo Castro Fallas",
                phone = configuration["Reports:Company:Phone"] ?? string.Empty,
                email = configuration["Reports:Company:Email"] ?? string.Empty,
                website = configuration["Reports:Company:Website"] ?? "https://logisticacastrofallas.com",
                logoDataUri = configuration["Reports:Company:LogoDataUri"] ?? string.Empty
            },
            generated = new
            {
                date = DateTime.UtcNow.ToString("dd/MM/yyyy"),
                time = DateTime.UtcNow.ToString("HH:mm")
            },
            originOffice = new
            {
                message = OriginOfficeMessage,
                polId = rate.PolId,
                polCode = rate.PolCode,
                polName = rate.PolName,
                qrContentType = "text/url",
                opensInternalSystem = false,
                publicPageUrl = originOfficePublicUrl,
                qrDataUri = originOfficeQrDataUri
            },
            rate = new
            {
                rateCode = rate.RateCode,
                quoteNumber = Text(rate.QuoNumber, rate.RateCode),
                idtraNumber = Text(rate.IdtraNumber, string.Empty),
                clientName = Text(rate.ClientName),
                agent = Text(rate.AgentName, "No asignado"),
                carrier = showCarrier ? Text(rate.CarrierName, "No asignada") : string.Empty,
                showCarrier,
                pol = rate.PolName,
                poe = rate.PoeName,
                pod = rate.PodName,
                route = $"{rate.PolName} → {rate.PodName} vía {rate.PoeName}",
                rateType = rate.RateType == Dhole.Pricing.Domain.Rates.Enums.RateType.Spot ? "SPOT" : "TARIFARIO",
                shipmentMode = rate.ShipmentMode.ToString(),
                containerType = shipmentSummary,
                containerQuantity = containers.Sum(x => x.quantity),
                containerSummary = shipmentSummary,
                totalPackages = rate.TotalPackages,
                totalPallets = rate.TotalPallets,
                totalWeightKg = rate.TotalWeightKg,
                totalVolumeCbm = rate.TotalVolumeCbm,
                kgPerCbm = rate.KgPerCbm,
                chargeableQuantity = rate.ChargeableQuantity,
                currency = currencyValue,
                currencyCode = rate.CurrencyCode,
                freeDays = rate.FreeDays,
                transitTime = string.IsNullOrWhiteSpace(rate.TransitTime) ? "Por confirmar" : rate.TransitTime,
                transitDays = rate.TransitTime,
                validFrom = rate.ValidFrom.ToString("dd/MM/yyyy"),
                validTo = rate.ValidTo.ToString("dd/MM/yyyy"),
                total = Money(reportDetails.Sum(detail => detail.SaleAmount * detail.Quantity)),
                totalAmount = reportDetails.Sum(detail => detail.SaleAmount * detail.Quantity),
                includes = commercialTerms.Includes,
                subjectTo = commercialTerms.SubjectTo,
                excludes = commercialTerms.Excludes,
                status = rate.Status.ToString(),
                rejectionReason = rate.Status == RateStatus.RejectedByClient
                    ? Text(rate.ClosedReason, string.Empty)
                    : string.Empty
            },
            containers,
            items,
            rows
        };

        return JsonSerializer.Serialize(data, JsonOptions);
    }

    private string CreateOriginOfficePublicUrl(RateHeader rate)
    {
        var baseAddress = (configuration["Reports:PublicWebBaseAddress"] ?? "https://dhole.customcodecr.com")
            .Trim()
            .TrimEnd('/');
        var polName = rate.PolName.Trim();
        var destinationName = !string.IsNullOrWhiteSpace(rate.PodName)
            ? rate.PodName.Trim()
            : rate.PoeName.Trim();
        var routeKey = $"{polName} - {destinationName}";

        return $"{baseAddress}/origin"
            + $"?pol={Uri.EscapeDataString(polName)}"
            + $"&shipmentMode={Uri.EscapeDataString(rate.ShipmentMode.ToString())}"
            + $"&route={Uri.EscapeDataString(routeKey)}";
    }

    private static string CreateQrDataUri(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(14);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private static (string Includes, string SubjectTo, string Excludes) ExclusiveCommercialTerms(
        string? includes,
        string? subjectTo,
        string? excludes)
    {
        static string[] Lines(string? value) => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        static string Key(string value)
        {
            var normalized = Regex.Replace(value.Trim().ToUpperInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
            var qualifier = Regex.Match(normalized, @"\s(?:USD|EUR|CRC|IVI|IVA|ITBMS|\d)");
            return qualifier.Success && qualifier.Index > 0
                ? normalized[..qualifier.Index].Trim()
                : normalized;
        }

        var included = Lines(includes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var includedKeys = included.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subject = Lines(subjectTo)
            .Where(item => !includedKeys.Contains(Key(item)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var subjectKeys = subject.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded = Lines(excludes)
            .Where(item => !includedKeys.Contains(Key(item)) && !subjectKeys.Contains(Key(item)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (string.Join(", ", included), string.Join(", ", subject), string.Join(", ", excluded));
    }
}

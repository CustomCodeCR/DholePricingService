using System.Globalization;
using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.Infrastructure.Reports;

public sealed class RateReportDataFactory(IConfiguration configuration) : IRateReportDataFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("en-US");

    public string CreateDataJson(RateHeader rate)
    {
        string Money(decimal amount) => $"{rate.CurrencyCode} {amount.ToString("N2", MoneyCulture)}";
        string Text(string? value, string fallback = "No especificado") =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        var items = rate.RateDetails
            .OrderBy(x => x.CostDetailType)
            .ThenBy(x => x.Name)
            .Select(detail => new
            {
                description = detail.Name,
                quantity = detail.Quantity,
                unitSale = Money(detail.SaleAmount),
                unitSaleAmount = detail.SaleAmount,
                lineTotal = Money(detail.SaleAmount * detail.Quantity),
                lineTotalAmount = detail.SaleAmount * detail.Quantity,
                notes = Text(detail.Notes, string.Empty)
            })
            .ToArray();

        var rows = rate.RateDetails
            .OrderBy(x => x.CostDetailType)
            .ThenBy(x => x.Name)
            .Select(detail => new Dictionary<string, object?>
            {
                ["Concepto"] = detail.Name,
                ["Cantidad"] = detail.Quantity,
                ["Moneda"] = rate.CurrencyCode,
                ["Precio unitario"] = detail.SaleAmount,
                ["Total"] = detail.SaleAmount * detail.Quantity,
                ["Notas"] = detail.Notes
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
            rate = new
            {
                rateCode = rate.RateCode,
                quoteNumber = Text(rate.QuoNumber, rate.RateCode),
                idtraNumber = Text(rate.IdtraNumber, string.Empty),
                clientName = Text(rate.ClientName),
                agent = Text(rate.AgentName, "No asignado"),
                carrier = Text(rate.CarrierName, "No asignada"),
                pol = rate.PolName,
                poe = rate.PoeName,
                pod = rate.PodName,
                route = $"{rate.PolName} → {rate.PodName} vía {rate.PoeName}",
                containerType = rate.ContainerTypeName,
                containerQuantity = rate.ContainerQuantity,
                currency = rate.CurrencyCode,
                freeDays = rate.FreeDays,
                transitTime = rate.TransitDays.HasValue ? $"{rate.TransitDays.Value} días" : "Por confirmar",
                transitDays = rate.TransitDays,
                validFrom = rate.ValidFrom.ToString("dd/MM/yyyy"),
                validTo = rate.ValidTo.ToString("dd/MM/yyyy"),
                total = Money(rate.TotalSaleAmount),
                totalAmount = rate.TotalSaleAmount,
                includes = Text(rate.Includes, string.Empty),
                subjectTo = Text(rate.SubjectTo, string.Empty),
                excludes = Text(rate.Excludes, string.Empty),
                status = rate.Status.ToString()
            },
            items,
            rows
        };

        return JsonSerializer.Serialize(data, JsonOptions);
    }
}

using System.Globalization;
using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Services;

namespace Dhole.Pricing.Infrastructure.ExchangeRates;

public sealed class HaciendaExchangeRateProvider(HttpClient httpClient) : IPricingExchangeRateProvider
{
    public async Task<PricingExchangeRateSnapshot?> GetUsdCrcAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var response = await httpClient.GetAsync("indicadores/tc/dolar", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (!TryReadRate(root, "compra", out var purchase, out var purchaseDate)
                || !TryReadRate(root, "venta", out var sale, out var saleDate)
                || purchase <= 0m
                || sale <= 0m)
            {
                return null;
            }

            var date = saleDate != default ? saleDate : purchaseDate;
            return new PricingExchangeRateSnapshot(
                Purchase: purchase,
                Sale: sale,
                RateDate: DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
                CapturedAtUtc: DateTime.UtcNow,
                Source: "Ministerio de Hacienda de Costa Rica"
            );
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<PricingEuroExchangeRateSnapshot?> GetEurUsdAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var response = await httpClient.GetAsync("indicadores/tc/euro", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (!TryReadPositiveDecimal(root, "dolares", out var dollars)
                || !TryReadPositiveDecimal(root, "colones", out var colones)
                || !root.TryGetProperty("fecha", out var dateNode)
                || dateNode.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(dateNode.GetString(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return null;
            }

            return new PricingEuroExchangeRateSnapshot(
                DollarsPerEuro: dollars,
                ColonesPerEuro: colones,
                RateDate: DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                CapturedAtUtc: DateTime.UtcNow,
                Source: "Ministerio de Hacienda de Costa Rica"
            );
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadPositiveDecimal(JsonElement root, string name, out decimal value)
    {
        value = 0m;
        if (!root.TryGetProperty(name, out var node)) return false;
        if (node.ValueKind == JsonValueKind.Number)
            return node.TryGetDecimal(out value) && value > 0m;
        if (node.ValueKind == JsonValueKind.String)
            return decimal.TryParse(node.GetString(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out value) && value > 0m;
        return false;
    }

    private static bool TryReadRate(
        JsonElement root,
        string propertyName,
        out decimal value,
        out DateTime date
    )
    {
        value = 0m;
        date = default;

        if (!root.TryGetProperty(propertyName, out var node)) return false;
        if (!node.TryGetProperty("valor", out var valueNode)) return false;

        if (valueNode.ValueKind == JsonValueKind.Number)
        {
            if (!valueNode.TryGetDecimal(out value)) return false;
        }
        else if (!decimal.TryParse(
            valueNode.GetString(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value))
        {
            return false;
        }

        if (node.TryGetProperty("fecha", out var dateNode))
        {
            DateTime.TryParse(
                dateNode.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out date
            );
        }

        return true;
    }
}

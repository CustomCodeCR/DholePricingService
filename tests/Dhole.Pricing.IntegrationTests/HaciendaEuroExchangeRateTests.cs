using System.Net;
using System.Text;
using Dhole.Pricing.Infrastructure.ExchangeRates;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class HaciendaEuroExchangeRateTests
{
    [TestMethod]
    public async Task EuroRate_UsesDolaresForUsdAndPreservesPublicationDate()
    {
        using var http = CreateClient("""{"fecha":"2026-10-28","dolares":1.1137,"colones":506.97}""");
        var provider = new HaciendaExchangeRateProvider(http);
        var rate = await provider.GetEurUsdAsync();

        Assert.IsNotNull(rate);
        Assert.AreEqual(1.1137m, rate.DollarsPerEuro);
        Assert.AreEqual(506.97m, rate.ColonesPerEuro);
        Assert.AreEqual(new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), rate.RateDate);
    }

    [TestMethod]
    public async Task MarketConversion_EurToUsd_MultipliesByHaciendaDolares()
    {
        using var http = CreateClient("""{"fecha":"2026-10-28","dolares":1.1137,"colones":506.97}""");
        var provider = new PricingMarketCurrencyExchangeRateProvider(new HaciendaExchangeRateProvider(http));

        var quote = await provider.GetRateToUsdAsync("eur", new DateTime(2026, 10, 28));
        Assert.IsNotNull(quote);
        Assert.AreEqual("EUR", quote.SourceCurrency);
        Assert.AreEqual("USD", quote.TargetCurrency);
        Assert.AreEqual(1.1137m, quote.Rate);
        Assert.AreEqual(111.37m, decimal.Round(100m * quote.Rate, 2));
    }

    [DataTestMethod]
    [DataRow("""{"fecha":"2026-10-28","dolares":0,"colones":506.97}""")]
    [DataRow("""{"fecha":"2026-10-28","dolares":1.1137,"colones":-1}""")]
    [DataRow("""{"fecha":"fecha-invalida","dolares":1.1137,"colones":506.97}""")]
    [DataRow("""{"fecha":"2026-10-28","colones":506.97}""")]
    public async Task EuroRate_InvalidResponse_DoesNotInventConversion(string json)
    {
        using var http = CreateClient(json);
        var provider = new HaciendaExchangeRateProvider(http);
        Assert.IsNull(await provider.GetEurUsdAsync());
    }

    [TestMethod]
    public async Task EuroRate_HaciendaUnavailable_DoesNotInventConversion()
    {
        using var http = CreateClient("{}", HttpStatusCode.ServiceUnavailable);
        var provider = new HaciendaExchangeRateProvider(http);
        Assert.IsNull(await provider.GetEurUsdAsync());
    }

    private static HttpClient CreateClient(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpClient(new EuroHandler(json, status))
        {
            BaseAddress = new Uri("https://api.hacienda.go.cr/")
        };
    }

    private sealed class EuroHandler(string json, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(
                "https://api.hacienda.go.cr/indicadores/tc/euro",
                request.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}

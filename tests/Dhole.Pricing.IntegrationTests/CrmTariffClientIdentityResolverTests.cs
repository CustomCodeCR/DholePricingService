using System.Net;
using System.Text;
using Dhole.Pricing.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class CrmTariffClientIdentityResolverTests
{
    private sealed class FakeFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual("DholeCrmQuoteDelivery", name);
            return client;
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static CrmTariffClientIdentityResolver Resolver(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var client = new HttpClient(new RecordingHandler(respond)) {
            BaseAddress = new Uri("http://crm-api:8080/")
        };
        return new CrmTariffClientIdentityResolver(new FakeFactory(client),
            NullLogger<CrmTariffClientIdentityResolver>.Instance);
    }

    [TestMethod]
    public async Task InstitutionalLeadIgnoresOperationalAuthOwnerAndUsesCastroFallas()
    {
        var id = Guid.NewGuid();
        var json = $$"""
        {"items":[{"kind":"lead","id":"{{id}}","displayName":"TARIFARIO",
          "executiveUserId":"{{Guid.NewGuid()}}","executiveName":"Maurice Lang",
          "isCastroFallasExecutive":true}],"total":1}
        """;
        var resolver = Resolver(request => {
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            Assert.AreEqual("test-token", request.Headers.Authorization?.Parameter);
            StringAssert.Contains(request.RequestUri!.Query, $"selectedClientId={id:D}");
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var (client, error) = await resolver.ResolveAsync(id, "lead",
            "Bearer test-token", CancellationToken.None);
        Assert.IsNull(error);
        Assert.IsNotNull(client);
        Assert.AreEqual("TARIFARIO", client.DisplayName);
        Assert.AreEqual("Castro Fallas", client.ExecutiveName);
    }

    [TestMethod]
    public async Task AccountHasCanonicalExecutiveEvenWhenBrowserClaimsAnother()
    {
        var id = Guid.NewGuid();
        var json = $$"""
        {"items":[{"kind":"account","id":"{{id}}","displayName":"Cliente Final S.A.",
          "executiveName":"Vendedor CRM","isCastroFallasExecutive":false}],"total":1}
        """;
        var resolver = Resolver(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var (client, error) = await resolver.ResolveAsync(id, "account",
            "Bearer test-token", CancellationToken.None);
        Assert.IsNull(error);
        Assert.AreEqual("Cliente Final S.A.", client!.DisplayName);
        Assert.AreEqual("Vendedor CRM", client.ExecutiveName);
    }

    [TestMethod]
    public async Task CannotLinkInaccessibleOrMismatchedCrmIdentity()
    {
        var id = Guid.NewGuid();
        var forbidden = Resolver(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var (client, error) = await forbidden.ResolveAsync(id, "lead",
            "Bearer test-token", CancellationToken.None);
        Assert.IsNull(client);
        StringAssert.Contains(error!, "permiso");

        var mismatch = Resolver(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent($$"""
            {"items":[{"kind":"account","id":"{{id}}","displayName":"Incorrecto",
             "executiveName":"Alguien","isCastroFallasExecutive":false}]}
            """, Encoding.UTF8, "application/json")
        });
        (client, error) = await mismatch.ResolveAsync(id, "lead",
            "Bearer test-token", CancellationToken.None);
        Assert.IsNull(client);
        StringAssert.Contains(error!, "no está disponible");
    }
}

using System.Net;
using System.Net.Http;
using Dhole.Pricing.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class CrmQuoteDeliveryRecipientResolverTests
{
    private static readonly Guid AccountId = Guid.Parse("351b4847-4435-4409-9fd2-9b3668e3b7cf");
    private static readonly Guid ContactId = Guid.Parse("451b4847-4435-4409-9fd2-9b3668e3b7cf");

    private static CrmQuoteDeliveryRecipientResolver CreateResolver(
        string contactsJson, HttpStatusCode sellerStatus = HttpStatusCode.OK)
    {
        var handler = new LocalCrmHandler((request) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (request.Headers.Authorization?.Parameter != "sample-token")
                return Json(HttpStatusCode.Unauthorized, "{}");
            if (path.EndsWith("/assigned-executive", StringComparison.Ordinal))
                return Json(sellerStatus, """{"displayName":"Carolina Quirós"}""");
            if (path.EndsWith("/contacts", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, contactsJson);
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://crm-api:8080/") };
        return new CrmQuoteDeliveryRecipientResolver(
            new LocalCrmClientFactory(client),
            NullLogger<CrmQuoteDeliveryRecipientResolver>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string content)
        => new(code) { Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task ClientDelivery_UsesLinkedContactAndAccountSeller()
    {
        var contacts = System.Text.Json.JsonSerializer.Serialize(new[] { new { contact = new { id = ContactId, name = "María Solís", email = "maria@example.com" } } });
        var (recipient, error) = await CreateResolver(contacts)
            .ResolveAsync(AccountId, ContactId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(error);
        Assert.IsNotNull(recipient);
        Assert.AreEqual("Carolina Quirós", recipient.AssignedSellerName);
        Assert.AreEqual("María Solís", recipient.ContactName);
        Assert.AreEqual("maria@example.com", recipient.ContactEmail);
    }

    [TestMethod]
    public async Task ClientDelivery_RejectsUnrelatedContact()
    {
        var contacts = System.Text.Json.JsonSerializer.Serialize(new[] { new { contact = new { id = Guid.NewGuid(), name = "Other", email = "other@example.com" } } });
        var (recipient, error) = await CreateResolver(contacts)
            .ResolveAsync(AccountId, ContactId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(recipient);
        StringAssert.Contains(error!, "ya no está vinculado");
    }

    [TestMethod]
    public async Task ClientDelivery_RejectsContactWithoutValidEmail()
    {
        var contacts = System.Text.Json.JsonSerializer.Serialize(new[] { new { contact = new { id = ContactId, name = "No mail", email = "invalid" } } });
        var (recipient, error) = await CreateResolver(contacts)
            .ResolveAsync(AccountId, ContactId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(recipient);
        StringAssert.Contains(error!, "correo electrónico válido");
    }

    [TestMethod]
    public async Task SellerDelivery_DoesNotRequireCustomerContact()
    {
        var (recipient, error) = await CreateResolver("[]")
            .ResolveAsync(AccountId, null, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(error);
        Assert.IsNotNull(recipient);
        Assert.IsNull(recipient.ContactEmail);
        Assert.AreEqual("Carolina Quirós", recipient.AssignedSellerName);
    }

    [TestMethod]
    public async Task Delivery_RejectsUnauthorizedCrmAccount()
    {
        var (recipient, error) = await CreateResolver("[]", HttpStatusCode.Forbidden)
            .ResolveAsync(AccountId, ContactId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(recipient);
        StringAssert.Contains(error!, "verificar el vendedor asignado");
    }

    [TestMethod]
    public async Task Delivery_RejectsMissingBearerToken()
    {
        var (recipient, error) = await CreateResolver("[]")
            .ResolveAsync(AccountId, ContactId, null, CancellationToken.None);
        Assert.IsNull(recipient);
        StringAssert.Contains(error!, "iniciar sesión");
    }

    private sealed class LocalCrmHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class LocalCrmClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual("DholeCrmQuoteDelivery", name);
            return client;
        }
    }
}

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
        string contactsJson, HttpStatusCode sellerStatus = HttpStatusCode.OK,
        string preferenceJson = """{"deliveryTarget":"seller","contactId":null}""")
    {
        var handler = new LocalCrmHandler((request) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (request.Headers.Authorization?.Parameter != "sample-token")
                return Json(HttpStatusCode.Unauthorized, "{}");
            if (path.EndsWith("/assigned-executive", StringComparison.Ordinal))
                return Json(sellerStatus, """{"displayName":"Carolina Quirós"}""");
            if (path.EndsWith("/quote-delivery-preference", StringComparison.Ordinal))
                return Json(sellerStatus, preferenceJson);
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

    [TestMethod]
    public async Task FutureQuotation_UsesAccountClientPreference()
    {
        var preference = System.Text.Json.JsonSerializer.Serialize(new
        {
            deliveryTarget = "client", contactId = ContactId
        });
        var (target, contactId, error) = await CreateResolver("[]",
            preferenceJson: preference).ResolvePreferenceAsync(
                AccountId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(error);
        Assert.AreEqual("client", target);
        Assert.AreEqual(ContactId, contactId);
    }

    [TestMethod]
    public async Task FutureQuotation_DefaultsToSellerWhenAccountHasNoClientPreference()
    {
        var (target, contactId, error) = await CreateResolver("[]")
            .ResolvePreferenceAsync(AccountId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(error);
        Assert.AreEqual("seller", target);
        Assert.IsNull(contactId);
    }

    [TestMethod]
    public async Task FutureQuotation_FailsClosedWhenClientPreferenceHasNoContact()
    {
        var (target, contactId, error) = await CreateResolver("[]",
            preferenceJson: """{"deliveryTarget":"client","contactId":null}""")
            .ResolvePreferenceAsync(AccountId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(target);
        Assert.IsNull(contactId);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public async Task FutureQuotation_FailsClosedWhenCrmRejectsAccess()
    {
        var (target, contactId, error) = await CreateResolver("[]",
            sellerStatus: HttpStatusCode.Forbidden).ResolvePreferenceAsync(
                AccountId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(target);
        Assert.IsNotNull(error);
    }


    [TestMethod]
    public async Task FutureQuotation_ResolvesAllConfiguredContacts()
    {
        var contactId2 = Guid.Parse("551b4847-4435-4409-9fd2-9b3668e3b7cf");
        var preference = System.Text.Json.JsonSerializer.Serialize(new
        {
            deliveryTarget = "client", contactIds = new[] { ContactId, contactId2 }
        });
        var contacts = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { contact = new { id = ContactId, name = "María", email = "maria@example.com", status = "Active" } },
            new { contact = new { id = contactId2, name = "José", email = "jose@example.com", status = "Active" } }
        });
        var resolver = CreateResolver(contacts, preferenceJson: preference);
        var (target, ids, preferenceError) = await resolver.ResolveMultiPreferenceAsync(
            AccountId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(preferenceError);
        Assert.AreEqual("client", target);
        CollectionAssert.AreEquivalent(new[] { ContactId, contactId2 }, ids);
        var (recipients, deliveryError) = await resolver.ResolveManyAsync(
            AccountId, ids, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(deliveryError);
        Assert.IsNotNull(recipients);
        Assert.AreEqual("Carolina Quirós", recipients.AssignedSellerName);
        CollectionAssert.AreEquivalent(
            new[] { "maria@example.com", "jose@example.com" },
            recipients.Contacts.Select(c => c.Email).ToArray());
    }

    [TestMethod]
    public async Task FutureQuotation_FailsIfAnyContactWasRemoved()
    {
        var missing = Guid.NewGuid();
        var contacts = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { contact = new { id = ContactId, name = "María", email = "maria@example.com", status = "Active" } }
        });
        var (recipients, error) = await CreateResolver(contacts).ResolveManyAsync(
            AccountId, new[] { ContactId, missing }, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(recipients);
        StringAssert.Contains(error!, "ya no pertenecen");
    }

    [TestMethod]
    public async Task FutureQuotation_FailsClosedForDuplicateSavedContactIds()
    {
        var preference = System.Text.Json.JsonSerializer.Serialize(new
        {
            deliveryTarget = "client", contactIds = new[] { ContactId, ContactId }
        });
        var (target, ids, error) = await CreateResolver("[]", preferenceJson: preference)
            .ResolveMultiPreferenceAsync(AccountId, "Bearer sample-token", CancellationToken.None);
        Assert.IsNull(target);
        Assert.AreEqual(0, ids.Length);
        Assert.IsNotNull(error);
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

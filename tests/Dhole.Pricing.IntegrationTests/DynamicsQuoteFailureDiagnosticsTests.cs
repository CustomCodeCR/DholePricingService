using System.Net;
using System.Text.Json;
using Dhole.Pricing.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class DynamicsQuoteFailureDiagnosticsTests
{
    [TestMethod]
    public void CapturesMissingTemplateFieldInsteadOfGenericInvalidOperationException()
    {
        var (text, json) = DynamicsQuoteFailureDiagnostics.Format(
            "mapping",
            new InvalidOperationException("Published CRUD template is missing msdyn_invoicesetuptotals"),
            false);
        StringAssert.Contains(text, "msdyn_invoicesetuptotals");
        StringAssert.Contains(text, "DYNAMICS_MAPPING_INVALID");
        using var parsed = JsonDocument.Parse(json);
        Assert.AreEqual("mapping", parsed.RootElement.GetProperty("stage").GetString());
        Assert.IsFalse(parsed.RootElement.GetProperty("postAttempted").GetBoolean());
    }

    [TestMethod]
    public void NeverPersistsApiKeysFromAnExceptionMessage()
    {
        var (text, json) = DynamicsQuoteFailureDiagnostics.Format(
            "mapping", new InvalidOperationException("X-Dhole-Api-Key=EXPOSED_SECRET_VALUE"), false);
        Assert.IsFalse(text.Contains("EXPOSED_SECRET_VALUE", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("EXPOSED_SECRET_VALUE", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProviderFailureContainsHttpCodeWithoutAResponseBody()
    {
        var (text, json) = DynamicsQuoteFailureDiagnostics.Format(
            "execute", new HttpRequestException("Sensitive outgoing URL"), true,
            HttpStatusCode.Forbidden, "crud_write_forbidden", "Permiso denegado");
        Assert.IsFalse(text.Contains("Sensitive outgoing URL", StringComparison.Ordinal));
        using var parsed = JsonDocument.Parse(json);
        Assert.AreEqual(403, parsed.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.AreEqual("crud_write_forbidden", parsed.RootElement.GetProperty("providerCode").GetString());
        Assert.IsTrue(parsed.RootElement.GetProperty("postAttempted").GetBoolean());
    }

    [TestMethod]
    public void OnlyPreWriteErrorsAreSafeToEnqueueForRetry()
    {
        Assert.IsTrue(DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure("lookup_name_not_found"));
        Assert.IsTrue(DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure("crud_key_invalid"));
        Assert.IsTrue(DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure("production_writes_not_approved"));
        Assert.IsFalse(DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure("quote_total_partial_requires_reconciliation"));
        Assert.IsFalse(DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure("write_pending_reconciliation"));
    }

    [TestMethod]
    public void LookupFailureIdentifiesRelationshipWithoutLeakingClientOrIdtra()
    {
        var provider = """
            {"type":"about:blank","title":"Quote rejected",
             "code":"lookup_name_not_found","lookupInput":"new_idtra1",
             "lookupSearchField":"title","untrustedName":"SECRET-CLIENT"}
            """;
        var (input, field) = DynamicsQuoteFailureDiagnostics.ReadProviderLookup(provider);
        Assert.AreEqual("new_idtra1", input);
        Assert.AreEqual("title", field);
        var (description, diagnostic) = DynamicsQuoteFailureDiagnostics.Format(
            "execute", new HttpRequestException("SECRET-CLIENT"), false,
            HttpStatusCode.NotFound, "lookup_name_not_found", "Quote rejected",
            input, field);
        StringAssert.Contains(description, "new_idtra1");
        StringAssert.Contains(description, "title");
        Assert.IsFalse(diagnostic.Contains("SECRET-CLIENT", StringComparison.Ordinal));
        using var parsed = JsonDocument.Parse(diagnostic);
        Assert.AreEqual("new_idtra1",
            parsed.RootElement.GetProperty("lookupInput").GetString());
        Assert.AreEqual("title",
            parsed.RootElement.GetProperty("lookupSearchField").GetString());
    }

    [TestMethod]
    public void ReadsOnlyAllowlistedFieldsFromRemoteErrors()
    {
        var (code, title) = DynamicsQuoteFailureDiagnostics.ReadProviderError(
            """{"code":"crud_template_inactive","title":"Plantilla inactiva","stackTrace":"SECRET"}""");
        Assert.AreEqual("crud_template_inactive", code);
        Assert.AreEqual("Plantilla inactiva", title);
    }
}

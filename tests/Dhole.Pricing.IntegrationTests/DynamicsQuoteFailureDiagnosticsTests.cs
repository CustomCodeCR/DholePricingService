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
    [TestMethod]
    public void Http409ReconciliationShowsWhetherHeaderOrTotalLineIsUnconfirmed()
    {
        var header = DynamicsQuoteFailureDiagnostics.ReadProviderError(
            """{"state":"requires_reconciliation","stage":"quote","recordId":null,"replay":false}""");
        Assert.AreEqual("quote_header_requires_reconciliation", header.Code);
        StringAssert.Contains(header.Title!, "cabecera");
        var total = DynamicsQuoteFailureDiagnostics.ReadProviderError(
            """{"state":"requires_reconciliation","stage":"total",
                "recordId":"00000000-0000-0000-0000-000000000001","replay":false}""");
        Assert.AreEqual("quote_total_requires_reconciliation", total.Code);
        StringAssert.Contains(total.Title!, "línea de venta");
        Assert.IsFalse(DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure(total.Code));
        Assert.IsFalse(DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure(header.Code));

        var (display, diagnosticJson) = DynamicsQuoteFailureDiagnostics.Format(
            "execute", new HttpRequestException("sensitive request URL"), true,
            HttpStatusCode.Conflict, total.Code, total.Title);
        Assert.IsTrue(display.Contains("línea de venta", StringComparison.Ordinal));
        using var parsed = JsonDocument.Parse(diagnosticJson);
        Assert.IsTrue(parsed.RootElement.GetProperty("postAttempted").GetBoolean());
    }



    [TestMethod]
    public void TextLimit422IsAProvenPreWriteFailureWithFieldDiagnostic()
    {
        Assert.IsTrue(DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure(
            "quote_text_exceeds_dataverse_limit"));
        var response = """
            {"code":"quote_text_exceeds_dataverse_limit",
             "lookupInput":"name","lookupSearchField":"MaxLength"}
            """;
        var (field, bound) = DynamicsQuoteFailureDiagnostics.ReadProviderLookup(response);
        Assert.AreEqual("name", field);
        Assert.AreEqual("MaxLength", bound);
        var (message, json) = DynamicsQuoteFailureDiagnostics.Format(
            "execute", new HttpRequestException("no POST"), false,
            HttpStatusCode.UnprocessableEntity, "quote_text_exceeds_dataverse_limit",
            "Validation rejected", field, bound);
        StringAssert.Contains(message, "name");
        using var parsed = JsonDocument.Parse(json);
        Assert.IsFalse(parsed.RootElement.GetProperty("postAttempted").GetBoolean());
    }

    [TestMethod]
    public void MissingDynamicsCustomerHasActionableSafePreWriteDiagnostic()
    {
        var response = """
            {"type":"about:blank","code":"quote_customer_required",
             "lookupInput":"customerid","lookupSearchField":"name",
             "untrustedName":"AUTOMANIA"}
            """;
        var (field, searchField) =
            DynamicsQuoteFailureDiagnostics.ReadProviderLookup(response);
        Assert.AreEqual("customerid", field);
        Assert.AreEqual("name", searchField);
        Assert.IsTrue(DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure(
            "quote_customer_required"));
        var (display, diagnostic) = DynamicsQuoteFailureDiagnostics.Format(
            "execute", new HttpRequestException("sensitive URL"), false,
            HttpStatusCode.UnprocessableEntity, "quote_customer_required",
            "Quote rejected", field, searchField);
        StringAssert.Contains(display, "customerid_account");
        StringAssert.Contains(display, "cliente");
        Assert.IsFalse(diagnostic.Contains("AUTOMANIA", StringComparison.Ordinal));
        using var parsed = JsonDocument.Parse(diagnostic);
        Assert.IsFalse(parsed.RootElement.GetProperty("postAttempted").GetBoolean());
    }

    [TestMethod]
    public void PriorTextLimitPreflightCanBeRetriedButOtherPostResultsCannot()
    {
        const string oldResponse = """
            {"stage":"execute","providerCode":"quote_text_exceeds_dataverse_limit",
             "httpStatus":422,"postAttempted":true}
            """;
        Assert.IsTrue(DynamicsQuoteFailureDiagnostics.IsKnownLegacyTextPreflight(oldResponse));
        Assert.IsFalse(DynamicsQuoteFailureDiagnostics.IsKnownLegacyTextPreflight(
            oldResponse.Replace("422", "409")));
        Assert.IsFalse(DynamicsQuoteFailureDiagnostics.IsKnownLegacyTextPreflight(
            oldResponse.Replace("quote_text_exceeds_dataverse_limit", "quote_header_dataverse_rejected")));
        Assert.IsFalse(DynamicsQuoteFailureDiagnostics.IsKnownLegacyTextPreflight(
            oldResponse.Replace("execute", "total")));
    }
}

using System.Text.RegularExpressions;
using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestReturnEmailTests
{
    private static RateRequest Request(string clientName = "CLIENTE GENERICO", string sellerName = "Vendedor")
        => RateRequest.Create(
            RateRequestPriority.Red,
            sellerUserId: Guid.NewGuid(),
            sellerName: sellerName,
            sellerEmail: "ventas@example.com",
            clientName: clientName,
            executiveName: "Ejecutivo",
            shipmentMode: "Fcl",
            originName: "Shanghai, China",
            destinationName: "San José, Costa Rica",
            payloadJson: "{\"form\":{\"clientName\":\"CLIENTE GENERICO\"}}");

    [TestMethod]
    public void Email_UsesReadableSubjectAndStructuredHtmlWithoutVisibleUuid()
    {
        var request = Request();
        var result = RateRequestReturnEmail.Compose(
            request, "Indicar la cantidad de bultos para continuar.",
            "https://sistema.logisticacastrofallas.com");

        Assert.AreEqual("Acción requerida: tarifa devuelta por Pricing · CLIENTE GENERICO", result.Subject);
        StringAssert.Contains(result.HtmlBody, "MOTIVO DE DEVOLUCIÓN");
        StringAssert.Contains(result.HtmlBody, "Indicar la cantidad de bultos para continuar.");
        StringAssert.Contains(result.HtmlBody, "Ruta:");
        StringAssert.Contains(result.HtmlBody, "Corregir y reenviar solicitud");
        StringAssert.Contains(result.HtmlBody, "https://sistema.logisticacastrofallas.com/pricing/request-rate/");
        StringAssert.Contains(result.HtmlBody, request.Id.ToString("D"));
        var visibleText = Regex.Replace(result.HtmlBody, "<[^>]*>", string.Empty);
        Assert.IsFalse(visibleText.Contains(request.Id.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.Subject.Contains(request.Id.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.SystemBody.Contains(request.Id.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Email_EncodesUserProvidedTextAndPreservesLineBreaks()
    {
        var request = Request("<script>alert('danger')</script>", "<b>Ventas</b>");
        var reason = "Falta la información <script>alert('danger')</script>\nRevisar la factura.";
        var result = RateRequestReturnEmail.Compose(
            request, reason, "https://dhole.customcodecr.com");

        Assert.IsFalse(result.HtmlBody.Contains("<script>", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.HtmlBody.Contains("<b>Ventas</b>", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(result.HtmlBody, "&lt;script&gt;");
        StringAssert.Contains(result.HtmlBody, "Revisar la factura.");
        StringAssert.Contains(result.HtmlBody, "<br />");
        StringAssert.Contains(result.HtmlBody, "https://dhole.customcodecr.com/pricing/request-rate/");
    }

    [TestMethod]
    public void Email_FallsBackToSalesInstructionsIfPublicPortalUrlIsUnsafeOrMissing()
    {
        var request = Request();
        foreach (var url in new[] { (string?)null, "http://example.com", "javascript:alert(1)" })
        {
            var result = RateRequestReturnEmail.Compose(request, "Faltan datos de carga.", url);
            StringAssert.Contains(result.HtmlBody, "Ingresá al módulo");
            Assert.IsFalse(result.HtmlBody.Contains("href=", StringComparison.OrdinalIgnoreCase));
        }
    }
    [TestMethod]
    public void Email_UsesCastroFallasBrandingWithoutGreenPalette()
    {
        var result = RateRequestReturnEmail.Compose(Request(), "Completar documentación.", "https://sistema.logisticacastrofallas.com");

        StringAssert.Contains(result.HtmlBody, "background-color:#a80e18");
        StringAssert.Contains(result.HtmlBody, "cid:dhole-castro-fallas-logo");
        StringAssert.Contains(result.HtmlBody, "alt=\"Castro Fallas\"");
        Assert.IsFalse(result.HtmlBody.Contains("#123f34", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.HtmlBody.Contains("#14765c", StringComparison.OrdinalIgnoreCase));
    }

}

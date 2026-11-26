using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class CommercialAllStatusExportTests
{
    private static object? Invoke(string method, params object?[] args) =>
        typeof(CommercialRateReportEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args);

    [TestMethod]
    [DataRow(RateStatus.Expired, false, "Expired", "Vencida")]
    [DataRow(RateStatus.Open, false, "Open", "Abierta")]
    [DataRow(RateStatus.Cancelled, false, "Cancelled", "Cancelada")]
    [DataRow(RateStatus.QUOVinculada, false, "QUOVinculada", "Cliente aceptó otra QUO")]
    [DataRow(RateStatus.Open, true, "PendingReapproval", "Aprobada pendiente de re-aprobación")]
    [DataRow(RateStatus.RequestedByClient, false, "Sent", "Enviada")]
    [DataRow(RateStatus.Closed, false, "RejectedByClient", "No aceptada")]
    public void ExportStatusUsesCommercialLabels(RateStatus status, bool reapproval, string expected, string label)
    {
        Assert.AreEqual(expected, (string)Invoke("ExportStatus", status, reapproval)!);
        Assert.AreEqual(label, (string)Invoke("StatusLabel", expected)!);
    }

    [TestMethod]
    public void AllStatusesIsOptInAndMultipleSelectionsAreValidated()
    {
        Assert.IsFalse((bool)Invoke("ValidStatus", "Expired")!);
        Assert.IsTrue((bool)Invoke("ValidExportStatuses", "Expired,Open,PendingReapproval")!);
        Assert.IsTrue((bool)Invoke("ValidExportStatuses", "")!);
        Assert.IsFalse((bool)Invoke("ValidExportStatuses", "Expired,unknown")!);
    }

    [TestMethod]
    public void CategoryWorkbookHasExactlyNineColumns()
    {
        var quoteType = typeof(CommercialRateReportEndpoints).GetNestedType("CommercialQuote", BindingFlags.NonPublic)!;
        var bytes = (byte[])Invoke("BuildCategoryWorkbook", Array.CreateInstance(quoteType, 0))!;
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        CollectionAssert.AreEqual(new[] { "TARIFA", "IDTRA", "Cliente", "Vendedor", "Estado", "Vigente desde", "Vigente hasta", "Fecha cierre", "Motivo cierre" }, xml.Descendants(ns + "c").Select(cell => cell.Value).ToArray());
        Assert.AreEqual("A1:I1", xml.Root!.Element(ns + "autoFilter")!.Attribute("ref")!.Value);
    }

    [TestMethod]
    [DataRow("pricing.rate.report.export", true, true)]
    [DataRow("pricing.rate.report.export", false, false)]
    [DataRow("pricing.rate-request.view-all", true, true)]
    [DataRow("pricing.rate-request.view-all", false, true)]
    [DataRow("pricing.rate.report.generate", true, false)]
    public void CategoryScopeDoesNotGrantFullFinancialReport(string scope, bool category, bool expected)
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim("scope", scope) }, "test"));
        var visibility = new Dhole.Pricing.Api.Services.SellerVisibilityService(null!);
        Assert.AreEqual(expected, (bool)Invoke("CanExportReport", visibility, principal, category)!);
    }

    [TestMethod]
    public void WorkbookKeepsAll23ColumnsAndFilterEvenWithNoMatches()
    {
        var quoteType = typeof(CommercialRateReportEndpoints)
            .GetNestedType("CommercialQuote", BindingFlags.NonPublic)!;
        var bytes = (byte[])Invoke("BuildWorkbook", Array.CreateInstance(quoteType, 0))!;
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells = xml.Descendants(ns + "c").Select(cell => cell.Value).ToArray();
        Assert.AreEqual(23, cells.Length);
        Assert.AreEqual("Estado", cells[16]);
        Assert.AreEqual("Vigente hasta", cells[20]);
        Assert.AreEqual("A1:W1", xml.Root!.Element(ns + "autoFilter")!.Attribute("ref")!.Value);
    }
}

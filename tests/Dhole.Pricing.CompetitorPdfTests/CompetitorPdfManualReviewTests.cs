using System.Reflection;
using Dhole.Pricing.Api.Services;

namespace Dhole.Pricing.CompetitorPdfTests;

[TestClass]
public sealed class CompetitorPdfManualReviewTests
{
    [DataTestMethod]
    [DataRow("""{"RequiresManualReview":"true","OceanFreight":"1245"}""", true)]
    [DataRow("""{"RequiresManualReview":true,"OceanFreight":"1245"}""", true)]
    [DataRow("""{"RequiresManualReview":"false","OceanFreight":"7395"}""", false)]
    [DataRow("""{"OceanFreight":"7395"}""", false)]
    public void SingleContainerRate_RespectsExplicitPdfReviewMarker(
        string sourceJson, bool expectedReview)
    {
        Assert.AreEqual(expectedReview, HasManualReview(sourceJson));
    }

    [TestMethod]
    public void MultiContainerIntegrityRule_RemainsIntact()
    {
        Assert.IsTrue(HasManualReview(
            """{"MultiContainerQuotation":"true","Quantity":"3","UnitPricesNormalized":"false"}"""));
        Assert.IsFalse(HasManualReview(
            """{"MultiContainerQuotation":"true","Quantity":"3","UnitPricesNormalized":"true","RequiresManualReview":"false"}"""));
        Assert.IsTrue(HasManualReview(
            """{"MultiContainerQuotation":"true","Quantity":"3","UnitPricesNormalized":"true","RequiresManualReview":"true"}"""));
    }

    private static bool HasManualReview(string json)
    {
        var method = typeof(CompetitorTariffImportProcessor).GetMethod(
            "ReadMultiContainerMetadata", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);
        var result = method.Invoke(null, [json]);
        Assert.IsNotNull(result);
        var property = result.GetType().GetProperty("RequiresManualReview");
        Assert.IsNotNull(property);
        return (bool)(property.GetValue(result) ?? false);
    }
}

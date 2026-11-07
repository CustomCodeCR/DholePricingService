using System.Reflection;
using Dhole.Pricing.Infrastructure.Reports;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateReportCommercialTermsLegacyNewlinesTests
{
    [TestMethod]
    public void CommercialTerms_LiteralEscapedNewlines_AreNormalizedInExistingQuotePdfs()
    {
        var normalize = typeof(RateReportDataFactory).GetMethod(
            "ExclusiveCommercialTerms", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(normalize);

        // Old multi-QUO drafts stored two literal characters (backslash + n)
        // instead of actual line breaks, which reached the PDF unchanged.
        var result = normalize.Invoke(null, new object?[]
        {
            @"Flete internacional\nManejos\nHBL",
            "Inspecciones\nSujeto a disponibilidad",
            "Impuestos\nSeguro de carga",
        });

        Assert.IsNotNull(result);
        var terms = ((string Includes, string SubjectTo, string Excludes))result;
        Assert.AreEqual("Flete internacional, Manejos, HBL", terms.Includes);
        Assert.AreEqual("Inspecciones, Sujeto a disponibilidad", terms.SubjectTo);
        Assert.AreEqual("Impuestos, Seguro de carga", terms.Excludes);
        Assert.IsFalse(terms.Includes.Contains(@"\n", StringComparison.Ordinal));
    }
}

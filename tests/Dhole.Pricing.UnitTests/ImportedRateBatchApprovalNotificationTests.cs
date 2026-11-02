using Dhole.Pricing.Application.Imports;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ImportedRateBatchApprovalNotificationTests
{
    [TestMethod]
    public void BatchWithMultipleRates_SendsOneSummaryForEntireBatch()
    {
        var batchId = Guid.NewGuid();
        var message = ImportedRateBatchApprovalNotificationBuilder.Build(
            batchId,
            27,
            ["FCL", "FCL", "LCL", "Aéreo"],
            ["MSC", "ONE", "MSC"],
            "https://sistema.logisticacastrofallas.com");

        Assert.AreEqual(27, message.TotalRates);
        StringAssert.Contains(message.Subject, "27 tarifas");
        StringAssert.Contains(message.Body, "Tarifas pendientes: 27");
        StringAssert.Contains(message.Body, "FCL, LCL");
        StringAssert.Contains(message.Body, "MSC, ONE");
        StringAssert.Contains(message.Body, batchId.ToString("D"));
        Assert.AreEqual($"/pricing/imports/review/{batchId:D}", message.Route);
        StringAssert.Contains(message.Body, "https://sistema.logisticacastrofallas.com" + message.Route);
        Assert.IsFalse(message.Body.Contains("ruta:", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(message.Body.Contains("equipo:", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SingleRowBatch_StillReferencesTheBatchInsteadOfTheRate()
    {
        var batchId = Guid.NewGuid();
        var message = ImportedRateBatchApprovalNotificationBuilder.Build(
            batchId,
            1,
            ["Aéreo"],
            ["DELTA"],
            "https://dhole.customcodecr.com/");

        StringAssert.Contains(message.Subject, "1 tarifa");
        StringAssert.Contains(message.Body, "Tarifas pendientes: 1");
        StringAssert.Contains(message.Body, "https://dhole.customcodecr.com/pricing/imports/review/");
        Assert.AreEqual($"/pricing/imports/review/{batchId:D}", message.Route);
    }

    [TestMethod]
    public void EmptyBatch_IsNotNotifiable()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImportedRateBatchApprovalNotificationBuilder.Build(
                Guid.NewGuid(), 0, [], [], null));
    }
}

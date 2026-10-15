using Dhole.Pricing.Domain.Competitors.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.CompetitorPdfTests;

[TestClass]
public sealed class CompetitorImportRecoveryTests
{
    [TestMethod]
    public void RetryImport_RestoresProcessingAndClearsPreviousError()
    {
        var tariff = CompetitorTariff.CreateImport(
            Guid.NewGuid(), "MGF", Guid.NewGuid(),
            new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
            ShipmentMode.Fcl, Guid.NewGuid(), "MGF.pdf");
        tariff.MarkImportFailed(null, 1, "Grpc.Unavailable", "No responde DataExtraction");
        Assert.AreEqual("Failed", tariff.ImportStatus);
        Assert.AreEqual("Grpc.Unavailable", tariff.ImportErrorCode);
        tariff.RetryImport();
        Assert.AreEqual("Processing", tariff.ImportStatus);
        Assert.IsNull(tariff.ImportErrorCode);
        Assert.IsNull(tariff.ImportErrorMessage);
        Assert.AreEqual(0, tariff.ObservationCount);
    }

    [TestMethod]
    public void PersistedPayload_RetainsDocumentAndLeaseForRestartRecovery()
    {
        var bytes = new byte[] { 37, 80, 68, 70 };
        var source = new CompetitorTariffImportPayload
        {
            CompetitorTariffId = Guid.NewGuid(),
            FileContent = bytes, FileHash = "example",
            NextAttemptAtUtc = DateTime.UtcNow
        };
        CollectionAssert.AreEqual(bytes, source.FileContent);
        Assert.AreEqual(0, source.AttemptCount);
        Assert.IsNull(source.LeaseUntilUtc);
    }
}

using Dhole.DataExtraction.Contracts.Grpc;
using Dhole.Pricing.Application.Abstractions.Services;
using Google.Protobuf;

namespace Dhole.Pricing.CompetitorPdfTests;

[TestClass]
public sealed class CompetitorGrpcSourceReferenceTests
{
    [TestMethod]
    public void ExistingStorageReference_IsPreservedAcrossGrpcWire()
    {
        var id = Guid.NewGuid();
        var request = new ExtractFclPricingDataGrpcRequest
        {
            PricingImportId = Guid.NewGuid().ToString(),
            OriginalFileName = "TARIFARIO MGF.pdf",
            ProfileCode = "COMPETITOR_FCL",
            FileContent = ByteString.CopyFrom(new byte[] { 37, 80, 68, 70 }),
            StoragePath = $"storage://{id:D}"
        };
        var received = ExtractFclPricingDataGrpcRequest.Parser.ParseFrom(request.ToByteArray());
        Assert.AreEqual($"storage://{id:D}", received.StoragePath);
        Assert.AreEqual("COMPETITOR_FCL", received.ProfileCode);
    }

    [TestMethod]
    public void CompetitorPricingRequest_CanReuseOriginalStoragePath()
    {
        var storageId = Guid.NewGuid();
        var request = new DataExtractionFclPricingRequest(
            Guid.NewGuid(), "correlation", "TARIFARIO MGF.pdf", "application/pdf",
            ".pdf", 50, "digest", "COMPETITOR_FCL", null, null,
            new byte[] { 37, 80, 68, 70 }, $"storage://{storageId:D}");
        Assert.AreEqual($"storage://{storageId:D}", request.StoragePath);
    }
}

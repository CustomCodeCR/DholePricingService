using System.Net;
using System.Net.Http.Headers;
using Dhole.Pricing.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class OpeningRateAttachmentReaderTests
{
    [TestMethod]
    public void IncludesRequestDocumentsAndFinalQuotationFilesWithoutDuplicates()
    {
        var requestId = Guid.NewGuid();
        var finalId = Guid.NewGuid();
        var references = OpeningRateAttachmentReader.GatherReferences(
            [requestId, finalId],
            [
                $$"""{"supportDocuments":[{"id":"{{requestId}}","fileName":"Packing List.pdf"}]}""",
                $$"""{"payload":{"supportDocuments":[{"id":"{{requestId}}","fileName":"duplicate.pdf"}]}}""",
                "{broken-json"
            ]);

        Assert.AreEqual(2, references.Count);
        Assert.AreEqual(requestId, references[0].Id);
        Assert.AreEqual("Packing List.pdf", references[0].FileName);
        Assert.AreEqual(finalId, references[1].Id);
    }

    [TestMethod]
    public void NoFilesStillAllowsTheCommercialPdf()
    {
        var references = OpeningRateAttachmentReader.GatherReferences([], ["{}"]);
        Assert.AreEqual(0, references.Count);
    }

    [TestMethod]
    public async Task ReadsStoredFileBytesAndPreservesOriginalFileName()
    {
        var fileId = Guid.NewGuid();
        var handler = new StubHandler();
        var reader = new OpeningRateAttachmentReader(
            new StubFactory(handler), NullLogger<OpeningRateAttachmentReader>.Instance);
        var result = await reader.ReadAsync(
            [new OpeningRateAttachmentReader.StorageReference(fileId, "backup.bin")],
            commercialPdfBytes: 500,
            CancellationToken.None);

        Assert.AreEqual(1, result.Documents.Count);
        Assert.AreEqual("Invoice.pdf", result.Documents[0].FileName);
        Assert.AreEqual("application/pdf", result.Documents[0].ContentType);
        CollectionAssert.AreEqual(new byte[] { 37, 80, 68, 70 }, result.Documents[0].Content);
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(0, result.UnavailableDocuments.Count);
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual("DholeStorageForOpenings", name);
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://storage.test/")
            };
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.IsTrue(request.RequestUri!.AbsolutePath.StartsWith(
                "/api/v1/storage/files/", StringComparison.Ordinal));
            var content = new ByteArrayContent([37, 80, 68, 70]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "Invoice.pdf"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }
}

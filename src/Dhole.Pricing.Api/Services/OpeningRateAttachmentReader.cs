using System.Net.Http.Headers;
using System.Text.Json;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Resolves the documents saved by Sales and Pricing before the notification enters
/// the durable outbox. Retries consequently send the same attachments as acceptance.
/// </summary>
public sealed class OpeningRateAttachmentReader(
    IHttpClientFactory clientFactory,
    ILogger<OpeningRateAttachmentReader> logger)
{
    // Includes the generated commercial PDF. Stay below normal SMTP message limits
    // after base64 encoding; Notifications permits the same file count.
    public const int MaxAttachments = 30;
    public const int MaxCombinedBytes = 14 * 1024 * 1024;

    public sealed record StorageReference(Guid Id, string? FileName);
    public sealed record EmailDocument(string FileName, string ContentType, byte[] Content);
    public sealed record ReadResult(
        IReadOnlyList<EmailDocument> Documents,
        IReadOnlyList<string> UnavailableDocuments);

    public static IReadOnlyList<StorageReference> GatherReferences(
        IEnumerable<Guid>? quotationStorageIds,
        IEnumerable<string?>? requestPayloads)
    {
        var result = new List<StorageReference>();
        var known = new HashSet<Guid>();

        void Add(Guid id, string? fileName)
        {
            if (id != Guid.Empty && known.Add(id))
                result.Add(new StorageReference(id, fileName));
        }

        if (requestPayloads is not null)
        {
            foreach (var json in requestPayloads)
            {
                if (string.IsNullOrWhiteSpace(json)) continue;
                try
                {
                    using var payload = JsonDocument.Parse(json);
                    ReadPayload(payload.RootElement);
                }
                catch (JsonException)
                {
                    // A legacy invalid request must not block all current attachments.
                }
            }
        }

        if (quotationStorageIds is not null)
            foreach (var id in quotationStorageIds) Add(id, null);

        return result;

        void ReadPayload(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return;

            if (Get(element, "supportDocuments", out var documents)
                && documents.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in documents.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!Get(item, "id", out var rawId)
                        && !Get(item, "storageId", out rawId)) continue;
                    if (rawId.ValueKind != JsonValueKind.String
                        || !Guid.TryParse(rawId.GetString(), out var fileId)) continue;
                    Add(fileId,
                        Get(item, "fileName", out var rawName)
                        && rawName.ValueKind == JsonValueKind.String
                            ? rawName.GetString()
                            : null);
                }
            }

            // Allow both current { supportDocuments, form } and legacy wrapped
            // { payload: { supportDocuments } } request serialization.
            if (Get(element, "payload", out var nested)
                && nested.ValueKind == JsonValueKind.Object)
                ReadPayload(nested);
            if (Get(element, "form", out var form)
                && form.ValueKind == JsonValueKind.Object)
                ReadPayload(form);
        }
    }

    public async Task<ReadResult> ReadAsync(
        IEnumerable<StorageReference> references,
        int commercialPdfBytes,
        CancellationToken cancellationToken)
    {
        var documents = new List<EmailDocument>();
        var unavailable = new List<string>();
        var availableBytes = MaxCombinedBytes - commercialPdfBytes;
        var client = clientFactory.CreateClient("DholeStorageForOpenings");

        foreach (var reference in references)
        {
            var fallbackName = SafeName(reference.FileName) ?? $"Documento-{reference.Id:N}";
            if (documents.Count + 1 >= MaxAttachments || availableBytes <= 0)
            {
                unavailable.Add(fallbackName + " (límite de adjuntos o tamaño)");
                continue;
            }

            try
            {
                using var response = await client.GetAsync(
                    $"api/v1/storage/files/{reference.Id:D}/content",
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Storage devolvió {Status} al recuperar documento {DocumentId} para apertura.",
                        (int)response.StatusCode, reference.Id);
                    unavailable.Add(fallbackName + " (no disponible en Storage)");
                    continue;
                }

                if (response.Content.Headers.ContentLength is long length
                    && length > availableBytes)
                {
                    unavailable.Add(fallbackName + " (supera el tamaño permitido)");
                    continue;
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                var tooLarge = false;
                int read;
                while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (read > availableBytes - buffer.Length)
                    {
                        tooLarge = true;
                        break;
                    }
                    await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                }

                if (tooLarge || buffer.Length == 0)
                {
                    unavailable.Add(fallbackName + (tooLarge
                        ? " (supera el tamaño permitido)" : " (archivo vacío)"));
                    continue;
                }

                var disposition = response.Content.Headers.ContentDisposition;
                var fileName = SafeName(disposition?.FileNameStar)
                    ?? SafeName(disposition?.FileName)
                    ?? SafeName(reference.FileName)
                    ?? fallbackName;
                var type = response.Content.Headers.ContentType?.MediaType;
                documents.Add(new EmailDocument(fileName,
                    string.IsNullOrWhiteSpace(type) ? "application/octet-stream" : type,
                    buffer.ToArray()));
                availableBytes -= (int)buffer.Length;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "No se pudo adjuntar documento {DocumentId} en el correo de apertura.",
                    reference.Id);
                unavailable.Add(fallbackName + " (error al descargar)");
            }
        }

        return new ReadResult(documents, unavailable);
    }

    private static string? SafeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Trim('"').Replace('\\', '/');
        var name = normalized[(normalized.LastIndexOf('/') + 1)..].Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static bool Get(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        value = default;
        return false;
    }
}

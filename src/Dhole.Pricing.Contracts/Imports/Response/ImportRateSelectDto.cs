namespace Dhole.Pricing.Contracts.Imports.Response;

public sealed record ImportRateSelectDto(
    Guid Id,
    Guid ImportBatchId,
    string SourceType,
    string Pol,
    string Pod,
    string Carrier,
    string ContainerType,
    string Currency,
    decimal Freight,
    int FreeDays,
    DateTime ValidFrom,
    DateTime ValidTo,
    string RawDataJson,
    string Status,
    int UsedAsRateCount,
    Guid? PolId = null,
    Guid? PoeId = null,
    string Poe = "",
    Guid? PodId = null,
    Guid? CarrierId = null,
    Guid? ContainerTypeId = null,
    string ContainerTypeCode = "",
    Guid? CurrencyId = null,
    decimal? TotalSale = null,
    int? TransitDays = null,
    [property: System.Text.Json.Serialization.JsonIgnore] string? RawSpaceComment = null
)
{
    private static readonly HashSet<string> CommentAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMENTARIOS DE ESPACIOS",
        "comentariosDeEspacios",
        "spaceComments",
        "spaceComment",
        "comentarioEspacios",
        "comentarioDeEspacios",
        "remarks",
        "observaciones",
        "comentarios",
        "comments",
        "comment"
    };

    /// <summary>
    /// Comentario operativo/comercial de la tarifa. Conserva el valor normalizado de la columna
    /// y, para importaciones históricas, intenta recuperarlo desde RawDataJson.
    /// </summary>
    public string? SpaceComment
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(RawSpaceComment))
                return RawSpaceComment.Trim();

            if (string.IsNullOrWhiteSpace(RawDataJson))
                return null;

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(RawDataJson);
                return FindComment(document.RootElement);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }

    private static string? FindComment(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (
                    CommentAliases.Contains(property.Name)
                    && property.Value.ValueKind == System.Text.Json.JsonValueKind.String
                )
                {
                    var text = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }

                var nested = FindComment(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindComment(child);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }
}

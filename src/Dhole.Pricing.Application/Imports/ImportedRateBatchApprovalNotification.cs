namespace Dhole.Pricing.Application.Imports;

/// <summary>
/// Customer-facing text for one notification per imported rate batch.
/// Intentionally does not select an individual rate as the batch's identity.
/// </summary>
public sealed record ImportedRateBatchApprovalNotification(
    string Subject,
    string Body,
    string Route,
    int TotalRates);

public static class ImportedRateBatchApprovalNotificationBuilder
{
    public static ImportedRateBatchApprovalNotification Build(
        Guid batchId,
        int totalRates,
        IEnumerable<string?> modalities,
        IEnumerable<string?> providers,
        string? publicWebBaseAddress)
    {
        if (batchId == Guid.Empty)
            throw new ArgumentException("El lote de tarifas es obligatorio.", nameof(batchId));
        if (totalRates <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalRates));

        var route = $"/pricing/imports/review/{batchId:D}";
        var webBase = string.IsNullOrWhiteSpace(publicWebBaseAddress)
            ? "https://sistema.logisticacastrofallas.com"
            : publicWebBaseAddress.Trim().TrimEnd('/');
        var link = $"{webBase}{route}";

        static string Summarize(IEnumerable<string?> items)
        {
            var values = items
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return values.Length == 0
                ? "Por definir"
                : values.Length <= 6
                    ? string.Join(", ", values)
                    : $"{string.Join(", ", values.Take(6))} y {values.Length - 6} más";
        }

        var label = totalRates == 1 ? "tarifa" : "tarifas";
        var subject = $"Lote de {totalRates} {label} pendiente de preaprobación";
        var body = $"Dhole recibió un lote de {totalRates} {label} por correo, "
            + "que pasó la preautorización automática y requiere revisión de Pricing.\n\n"
            + $"Lote: {batchId:D}\n"
            + $"Tarifas pendientes: {totalRates}\n"
            + $"Modalidades: {Summarize(modalities)}\n"
            + $"Proveedores: {Summarize(providers)}\n\n"
            + $"Revisar todas las tarifas del lote: {link}\n\n"
            + "Las tarifas del lote pueden revisarse y aprobarse o rechazarse desde una sola pantalla.";

        return new ImportedRateBatchApprovalNotification(subject, body, route, totalRates);
    }
}

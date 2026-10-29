namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Evita enviar a un destinatario específico los correos de tarifas cuyo
/// ejecutivo sea Castro Fallas. No altera las notificaciones de otros vendedores.
/// </summary>
public static class CastroFallasRateEmailRecipientPolicy
{
    private const string ExcludedRecipient = "jmesen@castrofallas.com";

    public static bool CanReceive(string? executiveName, string? recipientEmail)
    {
        if (string.IsNullOrWhiteSpace(executiveName))
            return true;

        var normalizedExecutive = string.Join(" ", executiveName.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return !string.Equals(normalizedExecutive, "Castro Fallas", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(recipientEmail?.Trim(), ExcludedRecipient, StringComparison.OrdinalIgnoreCase);
    }
}

using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.Rates;

/// <summary>
/// Parche temporal DH-20261120-INCOTERM-TEMP-UNBLOCK-353:
/// ningún Incoterm puede eliminar ni impedir el flete cotizado.
/// Los contratos públicos se mantienen para no romper a los consumidores.
/// TODO: restaurar asignación de responsabilidades sin bloquear servicios contratados.
/// </summary>
public static class RateQuotedFreightPolicy
{
    public static bool ExcludesQuotedFreight(
        RateOperationType operationType, string? incotermCode, string? incotermName) => false;

    public static bool ExcludesQuotedFreight(
        string? incotermCode, string? incotermName) => false;

    public static bool ExcludesQuotedFreight(
        ShipmentMode shipmentMode, string? incotermCode, string? incotermName) => false;
}

using System.Text.RegularExpressions;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.Rates;

/// <summary>
/// Determina cuándo la cotización no debe incluir flete internacional.
/// El campo Code puede ser un identificador opaco de Config; Name también
/// debe evaluarse para reconocer el Incoterm comercial seleccionado.
/// </summary>
public static class RateQuotedFreightPolicy
{
    private static readonly Regex NoFreightIncoterm = new(
        @"(?<![A-Za-z])(?:CFR|CIF|CPT|CIP|DAP|DPU|DDP)(?![A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // An explicitly priced DDP land movement includes international transportation.
    // The historical maritime exclusion (notably CIF) must remain unchanged.
    private static readonly Regex DdpIncoterm = new(
        @"(?<![A-Za-z])DDP(?![A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);


    // The person receiving the quotation determines which party would pay the
    // main carriage. The contractual Incoterm itself never changes direction.
    // Import: buyer pays EXW/FCA/FAS/FOB; export: seller pays CFR/CIF/etc.
    // Transit/domestic requires an explicit commercial allocation.
    private static readonly Regex BuyerPaysFreightIncoterm = new(
        @"(?<![A-Za-z])(?:EXW|FCA|FAS|FOB)(?![A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool ExcludesQuotedFreight(
        RateOperationType operationType, string? incotermCode, string? incotermName)
    {
        var term = $"{incotermCode} {incotermName}";
        return operationType switch
        {
            RateOperationType.Import => NoFreightIncoterm.IsMatch(term),
            RateOperationType.Export => BuyerPaysFreightIncoterm.IsMatch(term),
            _ => false,
        };
    }

    public static bool ExcludesQuotedFreight(string? incotermCode, string? incotermName) =>
        NoFreightIncoterm.IsMatch($"{incotermCode} {incotermName}");

    public static bool ExcludesQuotedFreight(
        ShipmentMode shipmentMode, string? incotermCode, string? incotermName) =>
        !(shipmentMode is ShipmentMode.Ltl or ShipmentMode.Ftl
            && DdpIncoterm.IsMatch($"{incotermCode} {incotermName}"))
        && ExcludesQuotedFreight(incotermCode, incotermName);
}

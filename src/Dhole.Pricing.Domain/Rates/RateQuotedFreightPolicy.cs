using System.Text.RegularExpressions;

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

    public static bool ExcludesQuotedFreight(string? incotermCode, string? incotermName) =>
        NoFreightIncoterm.IsMatch($"{incotermCode} {incotermName}");
}

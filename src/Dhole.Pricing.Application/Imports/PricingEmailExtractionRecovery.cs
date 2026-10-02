using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Imports;

public static class PricingEmailExtractionRecovery
{
    public static DataExtractionFclPricingResult Recover(
        DataExtractionFclPricingResult extraction,
        ImportSourceType sourceType,
        string? subject,
        string? originalFileName
    )
    {
        if (sourceType != ImportSourceType.Email || extraction.Rows.Count == 0)
        {
            return extraction;
        }

        var semanticSource = string.Join(
            "\n",
            new[]
            {
                subject,
                originalFileName,
                string.Join(
                    "\n",
                    extraction.Rows.Select(row =>
                        string.Join(
                            " | ",
                            new[]
                            {
                                row.SourceSheetName,
                                row.Remarks,
                                row.RawJson,
                            }.Where(value => !string.IsNullOrWhiteSpace(value))
                        )
                    )
                ),
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );

        var isNarrativeNac = semanticSource.Contains("NAC", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(
                semanticSource,
                @"WWL\s+CONTRACT.*(?:ONE[-/ ]MSC|MSC[-/ ]ONE)",
                RegexOptions.IgnoreCase
            );
        var isPier17Air = IsPier17AirSource(semanticSource);
        var airRoute = InferPier17AirRoute(semanticSource);
        var airValidity = InferTariffMonthValidity(
            string.Join(
                "\n",
                new[] { subject, originalFileName, semanticSource }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
            )
        );

        var rows = extraction.Rows
            .Select(row =>
            {
                var originPort = row.OriginPort;
                var portOfExit = !string.IsNullOrWhiteSpace(row.DestinationPort)
                    ? row.DestinationPort.Trim()
                    : row.PortOfExit?.Trim();
                var destinationPort = !string.IsNullOrWhiteSpace(row.DestinationPort)
                    ? null
                    : row.DestinationPort;
                var portOfExitReference = !string.IsNullOrWhiteSpace(row.DestinationPort)
                    ? row.DestinationPortReference ?? row.PortOfExitReference
                    : row.PortOfExitReference;
                var destinationPortReference = !string.IsNullOrWhiteSpace(row.DestinationPort)
                    ? null
                    : row.DestinationPortReference;
                var containerType = row.ContainerType;
                var carrier = row.Carrier;
                var agent = row.Agent;
                var currency = row.Currency;
                var oceanFreight = row.OceanFreight;
                var remarks = row.Remarks;
                var validFrom = row.ValidFrom;
                var validTo = row.ValidTo;
                var rowSemanticSource = string.Join(
                    "\n",
                    new[] { row.Remarks, row.RawJson }
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                );

                if (!string.IsNullOrWhiteSpace(row.DestinationPort))
                {
                    remarks = JoinRemarks(
                        remarks,
                        "POD de tarifa marítima persistido como POE."
                    );
                }

                if (isPier17Air)
                {
                    var recoveredOrigin = FirstText(
                        originPort,
                        ReadRawJsonValue(
                            row.RawJson,
                            "OriginPort",
                            "OriginAirport",
                            "Airport",
                            "Aeropuerto",
                            "POL",
                            "From"
                        ),
                        airRoute.Origin
                    );
                    var recoveredPoe = FirstText(
                        row.PortOfExit,
                        row.DestinationPort,
                        ReadRawJsonValue(
                            row.RawJson,
                            "PortOfExit",
                            "DestinationAirport",
                            "DestinationPort",
                            "POE",
                            "POD",
                            "To"
                        ),
                        airRoute.Destination
                    );

                    if (!SameText(originPort, recoveredOrigin))
                    {
                        originPort = recoveredOrigin;
                    }

                    if (!SameText(portOfExit, recoveredPoe))
                    {
                        portOfExit = recoveredPoe;
                        portOfExitReference = null;
                    }

                    destinationPort = null;
                    destinationPortReference = null;

                    if (!string.Equals(containerType, "AIR", StringComparison.OrdinalIgnoreCase))
                    {
                        containerType = "AIR";
                    }

                    carrier = FirstText(
                        carrier,
                        ReadRawJsonValue(
                            row.RawJson,
                            "Carrier",
                            "Airline",
                            "Aerolinea",
                            "Aerolínea"
                        )
                    );
                    agent = FirstText(agent, "Pier17");
                    currency = FirstText(
                        currency,
                        ReadRawJsonValue(row.RawJson, "Currency", "Moneda"),
                        string.Equals(airRoute.Origin, "MAD", StringComparison.OrdinalIgnoreCase)
                            ? "EUR"
                            : "USD"
                    );

                    if (!oceanFreight.HasValue)
                    {
                        oceanFreight = ReadRawJsonDecimal(
                            row.RawJson,
                            "OceanFreight",
                            "AirRatePlus100",
                            "Flete +100",
                            "Flete100",
                            "Rate +100",
                            "Rate100",
                            "+100",
                            "100"
                        );
                    }

                    validFrom ??= airValidity.ValidFrom;
                    validTo ??= airValidity.ValidTo;

                    var minimum = ReadRawJsonDecimal(
                        row.RawJson,
                        "MinimumRate",
                        "Minimum",
                        "Minimo",
                        "Mínimo"
                    );
                    var plus300 = ReadRawJsonDecimal(
                        row.RawJson,
                        "AirRatePlus300",
                        "Flete +300",
                        "Flete300",
                        "Rate +300",
                        "Rate300",
                        "+300",
                        "300"
                    );
                    var plus500 = ReadRawJsonDecimal(
                        row.RawJson,
                        "AirRatePlus500",
                        "Flete +500",
                        "Flete500",
                        "Rate +500",
                        "Rate500",
                        "+500",
                        "500"
                    );
                    var serviceMode = FirstText(
                        ReadRawJsonValue(
                            row.RawJson,
                            "ServiceMode",
                            "Service",
                            "Servicio",
                            "Modalidad"
                        ),
                        Regex.IsMatch(
                            rowSemanticSource,
                            @"\bB2B\b|\bback\s*[- ]?to\s*[- ]?back\b",
                            RegexOptions.IgnoreCase
                        )
                            ? "AIR_BACK_TO_BACK"
                            : Regex.IsMatch(
                                rowSemanticSource,
                                @"\bconsolidad[oa]\b|\bconsolidated\b",
                                RegexOptions.IgnoreCase
                            )
                                ? "AIR_CONSOLIDATED"
                                : null
                    );

                    remarks = JoinRemarks(
                        remarks,
                        BuildAirRecoveryRemark(
                            serviceMode,
                            minimum,
                            oceanFreight,
                            plus300,
                            plus500,
                            currency
                        )
                    );
                }

                if (string.IsNullOrWhiteSpace(containerType) && isNarrativeNac)
                {
                    containerType = "40HC";
                    remarks = JoinRemarks(
                        remarks,
                        "Equipo 40HC recuperado para oferta contractual narrativa MSC/ONE NAC."
                    );
                }

                if (ContainsEffectiveEtd(rowSemanticSource))
                {
                    if (validFrom.HasValue && !validTo.HasValue)
                    {
                        validTo = validFrom;
                        remarks = JoinRemarks(
                            remarks,
                            "Vigencia de un día recuperada desde Effective ETD."
                        );
                    }
                    else if (!validFrom.HasValue && validTo.HasValue)
                    {
                        validFrom = validTo;
                        remarks = JoinRemarks(
                            remarks,
                            "Vigencia de un día recuperada desde Effective ETD."
                        );
                    }
                }

                return row with
                {
                    OriginPort = originPort,
                    PortOfExit = portOfExit,
                    DestinationPort = destinationPort,
                    ContainerType = containerType,
                    Carrier = carrier,
                    Agent = agent,
                    Currency = currency,
                    OceanFreight = oceanFreight,
                    PortOfExitReference = portOfExitReference,
                    DestinationPortReference = destinationPortReference,
                    ContainerTypeReference = string.Equals(
                        containerType,
                        row.ContainerType,
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? row.ContainerTypeReference
                        : null,
                    CarrierReference = SameText(carrier, row.Carrier)
                        ? row.CarrierReference
                        : null,
                    AgentReference = SameText(agent, row.Agent)
                        ? row.AgentReference
                        : null,
                    CurrencyReference = SameText(currency, row.Currency)
                        ? row.CurrencyReference
                        : null,
                    ValidFrom = validFrom,
                    ValidTo = validTo,
                    Remarks = remarks,
                };
            })
            .ToArray();

        return extraction with { Rows = rows };
    }

    private static bool IsPier17AirSource(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(
                value,
                @"\bPIER\s*17\b|\bPIER17\b|pier17group\.com",
                RegexOptions.IgnoreCase
            )
            && Regex.IsMatch(
                value,
                @"\bA[EÉ]REO\b|\bAIR\b|\bKG\s*/\s*VOL\b|\bFlete\s*\+?100\b",
                RegexOptions.IgnoreCase
            );
    }

    private static (string? Origin, string? Destination) InferPier17AirRoute(string value)
    {
        var explicitRoute = Regex.Match(
            value,
            @"\b(?<from>[A-Z]{3})\b\s*\)?\s*(?:hacia|to|[-–])\s*\(?(?<to>[A-Z]{3})\b",
            RegexOptions.IgnoreCase
        );
        if (explicitRoute.Success)
        {
            return (
                explicitRoute.Groups["from"].Value.ToUpperInvariant(),
                explicitRoute.Groups["to"].Value.ToUpperInvariant()
            );
        }

        if (Regex.IsMatch(value, @"\bMIA\b|\bMIAMI\b", RegexOptions.IgnoreCase))
        {
            return ("MIA", "SJO");
        }

        if (
            Regex.IsMatch(
                value,
                @"\bMAD\b|\bMADRID\b|\bESPA[ÑN]A\b",
                RegexOptions.IgnoreCase
            )
        )
        {
            return ("MAD", "SJO");
        }

        return (null, null);
    }

    private static (DateTime? ValidFrom, DateTime? ValidTo) InferTariffMonthValidity(
        string value
    )
    {
        var match = Regex.Match(
            value,
            @"\b(?:tarifario|tariff|rates?)\b.{0,120}?\b(?<month>enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre|january|february|march|april|may|june|july|august|september|october|november|december)\b[\s/\-]*(?<year>20\d{2})\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        if (
            !match.Success
            || !TryParseMonth(match.Groups["month"].Value, out var month)
            || !int.TryParse(
                match.Groups["year"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var year
            )
        )
        {
            return (null, null);
        }

        return (
            new DateTime(year, month, 1),
            new DateTime(year, month, DateTime.DaysInMonth(year, month))
        );
    }

    private static bool TryParseMonth(string value, out int month)
    {
        month = value.Trim().ToLowerInvariant() switch
        {
            "enero" or "january" => 1,
            "febrero" or "february" => 2,
            "marzo" or "march" => 3,
            "abril" or "april" => 4,
            "mayo" or "may" => 5,
            "junio" or "june" => 6,
            "julio" or "july" => 7,
            "agosto" or "august" => 8,
            "septiembre" or "setiembre" or "september" => 9,
            "octubre" or "october" => 10,
            "noviembre" or "november" => 11,
            "diciembre" or "december" => 12,
            _ => 0,
        };

        return month > 0;
    }

    private static string? BuildAirRecoveryRemark(
        string? serviceMode,
        decimal? minimum,
        decimal? plus100,
        decimal? plus300,
        decimal? plus500,
        string? currency
    )
    {
        var items = new List<string>();
        if (!string.IsNullOrWhiteSpace(serviceMode))
        {
            items.Add($"Servicio aéreo: {serviceMode}");
        }

        items.Add("Base: KG/VOL");

        var ccy = string.IsNullOrWhiteSpace(currency) ? string.Empty : $" {currency}";
        if (minimum.HasValue)
        {
            items.Add($"Mínimo: {minimum.Value:0.####}{ccy}");
        }

        if (plus100.HasValue)
        {
            items.Add($"+100: {plus100.Value:0.####}{ccy}");
        }

        if (plus300.HasValue)
        {
            items.Add($"+300: {plus300.Value:0.####}{ccy}");
        }

        if (plus500.HasValue)
        {
            items.Add($"+500: {plus500.Value:0.####}{ccy}");
        }

        return items.Count > 1 ? string.Join("; ", items) + "." : null;
    }

    private static string? ReadRawJsonValue(string? rawJson, params string[] aliases)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return FindJsonValue(document.RootElement, aliases);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindJsonValue(
        JsonElement element,
        IReadOnlyCollection<string> aliases
    )
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (
                    aliases.Any(alias =>
                        NormalizeKey(property.Name).Equals(
                            NormalizeKey(alias),
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    && property.Value.ValueKind is not JsonValueKind.Null
                    && property.Value.ValueKind is not JsonValueKind.Undefined
                )
                {
                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ValueKind == JsonValueKind.Number
                            ? property.Value.GetRawText()
                            : null;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindJsonValue(property.Value, aliases);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindJsonValue(child, aliases);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static decimal? ReadRawJsonDecimal(string? rawJson, params string[] aliases)
    {
        var value = ReadRawJsonValue(rawJson, aliases);
        return ParseDecimal(value);
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = Regex.Replace(
            raw,
            @"(?:US\$|USD|EUR|CRC|[$€₡])",
            string.Empty,
            RegexOptions.IgnoreCase
        ).Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        if (value.Contains(',') && value.Contains('.'))
        {
            if (value.LastIndexOf(',') > value.LastIndexOf('.'))
            {
                value = value.Replace(".", string.Empty, StringComparison.Ordinal)
                    .Replace(',', '.');
            }
            else
            {
                value = value.Replace(",", string.Empty, StringComparison.Ordinal);
            }
        }
        else if (value.Contains(','))
        {
            var decimalDigits = value.Length - value.LastIndexOf(',') - 1;
            value = decimalDigits is 1 or 2
                ? value.Replace(',', '.')
                : value.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        return decimal.TryParse(
            value,
            NumberStyles.Number | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed
        ) && parsed >= 0m
            ? parsed
            : null;
    }

    private static string NormalizeKey(string value)
    {
        return new string(
            value
                .Normalize(System.Text.NormalizationForm.FormD)
                .Where(character =>
                    CharUnicodeInfo.GetUnicodeCategory(character)
                        != UnicodeCategory.NonSpacingMark
                )
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray()
        );
    }

    private static bool ContainsEffectiveEtd(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(
                value,
                @"\beffective\s*etd\b",
                RegexOptions.IgnoreCase
            );
    }

    private static string JoinRemarks(string? current, string? addition)
    {
        if (string.IsNullOrWhiteSpace(addition))
        {
            return current ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return addition;
        }

        return current.Contains(addition, StringComparison.OrdinalIgnoreCase)
            ? current
            : $"{current.Trim().TrimEnd('.')}. {addition}";
    }

    private static bool SameText(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

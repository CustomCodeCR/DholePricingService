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

        var rows = extraction.Rows
            .Select(row =>
            {
                var portOfExit = !string.IsNullOrWhiteSpace(row.DestinationPort)
                    ? row.DestinationPort.Trim()
                    : row.PortOfExit?.Trim();
                var portOfExitReference = !string.IsNullOrWhiteSpace(row.DestinationPort)
                    ? row.DestinationPortReference ?? row.PortOfExitReference
                    : row.PortOfExitReference;
                var containerType = row.ContainerType;
                var remarks = row.Remarks;

                if (!string.IsNullOrWhiteSpace(row.DestinationPort))
                {
                    remarks = JoinRemarks(
                        remarks,
                        "POD de tarifa marítima persistido como POE."
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

                return row with
                {
                    PortOfExit = portOfExit,
                    DestinationPort = null,
                    ContainerType = containerType,
                    PortOfExitReference = portOfExitReference,
                    DestinationPortReference = null,
                    Remarks = remarks,
                };
            })
            .ToArray();

        return extraction with { Rows = rows };
    }

    private static string JoinRemarks(string? current, string addition)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return addition;
        }

        return current.Contains(addition, StringComparison.OrdinalIgnoreCase)
            ? current
            : $"{current.Trim().TrimEnd('.')}. {addition}";
    }
}

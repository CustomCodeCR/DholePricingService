using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Contracts.Imports.Request;

namespace Dhole.Pricing.Application.Imports;

public static class DataExtractionPricingImportMapper
{
    public static DataExtractionFclPricingResult ToApplicationResult(
        ExtractedPricingDataRequest response,
        Guid extractionExecutionId,
        Guid pricingImportId
    )
    {
        return new DataExtractionFclPricingResult(
            response.Success,
            response.ExtractionExecutionId ?? extractionExecutionId,
            pricingImportId,
            response.CorrelationId,
            new DataExtractionFclPricingSummary(
                response.Summary.TotalRows,
                response.Summary.ValidRows,
                response.Summary.WarningRows,
                response.Summary.InvalidRows,
                response.Summary.HasIssues
            ),
            response.Rows.Select(ToApplicationRow).ToArray(),
            response.Issues.Select(ToApplicationIssue).ToArray(),
            response.ErrorCode,
            response.ErrorMessage,
            ToApplicationReference(response.ProfileReference)
        );
    }

    private static DataExtractionFclPricingRow ToApplicationRow(
        ExtractedPricingRowRequest row
    )
    {
        return new DataExtractionFclPricingRow(
            row.Id,
            row.SourceSheetName,
            row.SourceRowNumber,
            row.OriginPort,
            row.PortOfExit,
            row.DestinationPort,
            row.ContainerType,
            row.Carrier,
            row.Agent,
            row.Commodity,
            row.Currency,
            row.FreeDays,
            row.TransitDays,
            row.ValidFrom,
            row.ValidTo,
            row.OceanFreight,
            row.OriginCharges,
            row.DestinationCharges,
            row.Surcharges,
            row.TotalCost,
            row.TotalSale,
            row.Profit,
            row.Margin,
            row.SpaceComment,
            row.Remarks,
            row.Status,
            row.RawJson,
            ToApplicationReference(row.OriginPortReference),
            ToApplicationReference(row.PortOfExitReference),
            ToApplicationReference(row.DestinationPortReference),
            ToApplicationReference(row.ContainerTypeReference),
            ToApplicationReference(row.CarrierReference),
            ToApplicationReference(row.AgentReference),
            ToApplicationReference(row.CurrencyReference)
        );
    }

    private static DataExtractionFclPricingIssue ToApplicationIssue(
        ExtractedPricingIssueRequest issue
    )
    {
        return new DataExtractionFclPricingIssue(
            issue.Id,
            issue.ExtractedPricingRowId,
            issue.Code,
            issue.Message,
            issue.IsBlocking,
            issue.SourceSheetName,
            issue.SourceRowNumber,
            issue.ColumnName,
            issue.RawValue
        );
    }

    private static DataExtractionCatalogReference? ToApplicationReference(
        ExtractedCatalogReferenceRequest? reference
    )
    {
        return reference is null
            ? null
            : new DataExtractionCatalogReference(
                reference.Id,
                reference.CatalogGroupSlug,
                reference.Code,
                reference.Slug,
                reference.Name,
                reference.RawValue
            );
    }
}

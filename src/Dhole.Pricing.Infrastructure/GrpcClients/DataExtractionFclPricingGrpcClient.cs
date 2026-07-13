using System.Globalization;
using Dhole.DataExtraction.Contracts.Grpc;
using Dhole.Pricing.Application.Abstractions.Services;
using Google.Protobuf;
using Grpc.Core;

namespace Dhole.Pricing.Infrastructure.GrpcClients;

public sealed class DataExtractionFclPricingGrpcClient(
    DataExtractionGrpc.DataExtractionGrpcClient client
) : IDataExtractionFclPricingClient
{
    public async Task<DataExtractionFclPricingResult> ExtractAsync(
        DataExtractionFclPricingRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var grpcRequest = new ExtractFclPricingDataGrpcRequest
            {
                PricingImportId = request.PricingImportId.ToString(),
                CorrelationId = request.CorrelationId,
                OriginalFileName = request.OriginalFileName,
                ContentType = request.ContentType ?? string.Empty,
                FileExtension = request.FileExtension ?? string.Empty,
                FileSizeBytes = request.FileSizeBytes,
                FileHash = request.FileHash,
                ProfileCode = request.ProfileCode ?? string.Empty,
                RequestedBy = request.RequestedBy?.ToString() ?? string.Empty,
                RequestedByName = request.RequestedByName ?? string.Empty,
                FileContent = ByteString.CopyFrom(request.FileContent),
            };

            var response = await client.ExtractFclPricingDataAsync(
                grpcRequest,
                cancellationToken: cancellationToken
            );

            return new DataExtractionFclPricingResult(
                response.Success,
                TryParseGuid(response.ExtractionExecutionId),
                TryParseGuid(response.PricingImportId) ?? request.PricingImportId,
                string.IsNullOrWhiteSpace(response.CorrelationId)
                    ? request.CorrelationId
                    : response.CorrelationId,
                new DataExtractionFclPricingSummary(
                    response.Summary?.TotalRows ?? 0,
                    response.Summary?.ValidRows ?? 0,
                    response.Summary?.WarningRows ?? 0,
                    response.Summary?.InvalidRows ?? 0,
                    response.Summary?.HasIssues ?? false
                ),
                response.Records.Select(ToRow).ToArray(),
                response.Issues.Select(ToIssue).ToArray(),
                EmptyToNull(response.ErrorCode),
                EmptyToNull(response.ErrorMessage),
                ToReference(response.ProfileReference)
            );
        }
        catch (RpcException exception)
        {
            return new DataExtractionFclPricingResult(
                false,
                null,
                request.PricingImportId,
                request.CorrelationId,
                new DataExtractionFclPricingSummary(0, 0, 0, 0, true),
                Array.Empty<DataExtractionFclPricingRow>(),
                [
                    new DataExtractionFclPricingIssue(
                        Guid.NewGuid(),
                        null,
                        $"Grpc.{exception.StatusCode}",
                        exception.Status.Detail,
                        true,
                        null,
                        null,
                        null,
                        null
                    ),
                ],
                $"Grpc.{exception.StatusCode}",
                exception.Status.Detail,
                null
            );
        }
    }

    private static DataExtractionFclPricingRow ToRow(PricingExtractionRecordGrpcModel row)
    {
        return new DataExtractionFclPricingRow(
            TryParseGuid(row.Id) ?? Guid.NewGuid(),
            EmptyToNull(row.SourceSheetName),
            row.SourceRowNumber <= 0 ? null : row.SourceRowNumber,
            EmptyToNull(row.OriginPort),
            EmptyToNull(row.PortOfExit),
            EmptyToNull(row.DestinationPort),
            EmptyToNull(row.ContainerType),
            EmptyToNull(row.Carrier),
            EmptyToNull(row.Agent),
            EmptyToNull(row.Commodity),
            EmptyToNull(row.Currency),
            row.FreeDays < 0 ? null : row.FreeDays,
            row.TransitDays < 0 ? null : row.TransitDays,
            TryParseDate(row.ValidFrom),
            TryParseDate(row.ValidTo),
            ToNullableDecimal(row.HasOceanFreight, row.OceanFreight),
            ToNullableDecimal(row.HasOriginCharges, row.OriginCharges),
            ToNullableDecimal(row.HasDestinationCharges, row.DestinationCharges),
            ToNullableDecimal(row.HasSurcharges, row.Surcharges),
            ToNullableDecimal(row.HasTotalCost, row.TotalCost),
            ToNullableDecimal(row.HasTotalSale, row.TotalSale),
            ToNullableDecimal(row.HasProfit, row.Profit),
            ToNullableDecimal(row.HasMargin, row.Margin),
            EmptyToNull(row.SpaceComment),
            EmptyToNull(row.Remarks),
            string.IsNullOrWhiteSpace(row.Status) ? "Unknown" : row.Status,
            EmptyToNull(row.RawJson),
            ToReference(row.OriginPortReference),
            ToReference(row.PortOfExitReference),
            ToReference(row.DestinationPortReference),
            ToReference(row.ContainerTypeReference),
            ToReference(row.CarrierReference),
            ToReference(row.AgentReference),
            ToReference(row.CurrencyReference)
        );
    }

    private static DataExtractionFclPricingIssue ToIssue(ExtractionIssueGrpcModel issue)
    {
        return new DataExtractionFclPricingIssue(
            TryParseGuid(issue.Id) ?? Guid.NewGuid(),
            TryParseGuid(issue.PricingExtractionRecordId),
            string.IsNullOrWhiteSpace(issue.Code) ? "DataExtraction.Issue" : issue.Code,
            issue.Message,
            issue.IsBlocking,
            EmptyToNull(issue.SourceSheetName),
            issue.SourceRowNumber <= 0 ? null : issue.SourceRowNumber,
            EmptyToNull(issue.ColumnName),
            EmptyToNull(issue.RawValue)
        );
    }

    private static DataExtractionCatalogReference? ToReference(CatalogReferenceGrpcModel? reference)
    {
        if (reference is null || !reference.Resolved || !Guid.TryParse(reference.Id, out var id))
        {
            return null;
        }

        return new DataExtractionCatalogReference(
            id,
            reference.CatalogGroupSlug,
            reference.Code,
            reference.Slug,
            reference.Name,
            EmptyToNull(reference.RawValue)
        );
    }

    private static Guid? TryParseGuid(string? value)
    {
        return Guid.TryParse(value, out var guid) ? guid : null;
    }

    private static DateTime? TryParseDate(string? value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var date
        )
            ? date
            : null;
    }

    private static decimal? ToNullableDecimal(bool hasValue, double value)
    {
        return hasValue ? Convert.ToDecimal(value) : null;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

using System.Data;
using System.Globalization;
using System.Text.Json.Nodes;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Single source of truth for a MultiQUO PDF, used by downloads, outgoing
/// commercial emails and completion of seller requests. Never expose technical
/// RateCode sequences as separate commercial quotation numbers.
/// </summary>
public sealed class GroupCommercialQuotePdfService(
    ServiceDbContext db,
    IRateHeaderRepository rateHeaders,
    IRateCommentStore rateComments,
    IRateReportDataFactory dataFactory,
    IPricingReportsClient reportsClient)
{
    public async Task<GroupCommercialQuoteResult?> TryGenerateAsync(
        Guid rateId, CancellationToken cancellationToken)
    {
        var rows = new List<(Guid Id, Guid ParentId, string BaseQuote, string Suffix, string Status)>();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var query = connection.CreateCommand();
            query.CommandText = """
                SELECT a.rate_id, a.parent_rate_id, a.base_quote, a.suffix, a.status
                FROM pricing.rate_quote_alternatives a
                WHERE a.parent_rate_id = (
                    SELECT parent_rate_id FROM pricing.rate_quote_alternatives
                    WHERE rate_id = @rate_id LIMIT 1
                )
                ORDER BY length(a.suffix), a.suffix;
                """;
            var parameter = query.CreateParameter();
            parameter.ParameterName = "@rate_id";
            parameter.Value = rateId;
            query.Parameters.Add(parameter);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((reader.GetGuid(0), reader.GetGuid(1),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }

        if (rows.Count < 2) return null;
        var first = rows[0];
        var selected = rows.FirstOrDefault(row => row.Status == "Selected");
        if (selected.Id != Guid.Empty)
        {
            // Historical links to discarded sub-QUOs must use the definitive one.
            return new GroupCommercialQuoteResult(
                selected.Id, first.ParentId, first.BaseQuote, null);
        }

        var options = new List<PricingQuoteGroupDocumentItem>(rows.Count);
        foreach (var child in rows)
        {
            var rate = await rateHeaders.GetByIdWithDetailsAsync(child.Id, cancellationToken);
            if (rate is null || rate.IsDeleted)
                throw new GroupCommercialQuoteApprovalException("La MultiQUO tiene una alternativa inexistente.");
            if (rate.RequiredApproval || rate.Status is not (
                RateStatus.ApprovedByManagement or RateStatus.Open or RateStatus.Sent
                or RateStatus.AcceptedByClient or RateStatus.RejectedByClient
                or RateStatus.Closed or RateStatus.Expired))
                throw new GroupCommercialQuoteApprovalException(
                    "Apruebe el margen de todas las alternativas antes de generar o enviar el PDF.");

            var json = JsonNode.Parse(dataFactory.CreateDataJson(rate)) as JsonObject;
            if (json?["rate"] is JsonObject rateData)
                rateData["comments"] = (await rateComments.GetAsync(child.Id, cancellationToken))?.Trim() ?? string.Empty;

            var freight = rate.RateDetails
                .Where(detail => detail.CostDetailType == CostDetailType.Freight).ToArray();
            var ofAmount = freight.Sum(detail => detail.SaleAmount * detail.Quantity);
            var currency = QuoteCurrencyValue.Resolve(freight.FirstOrDefault()?.CurrencyName, rate.CurrencyName);
            var route = rate.PolName + " → " + rate.PoeName
                + (string.IsNullOrWhiteSpace(rate.PodName) ? "" : " → " + rate.PodName);
            if (!string.IsNullOrWhiteSpace(rate.CarrierRoute))
                route += " · Ruta naviera: " + rate.CarrierRoute.Trim();
            options.Add(new PricingQuoteGroupDocumentItem(
                QuoteAlternativeNumbering.SubQuoteNumber(child.BaseQuote, child.Suffix),
                route, rate.CarrierName,
                ofAmount.ToString("N2", CultureInfo.GetCultureInfo("en-US")),
                currency, json?.ToJsonString() ?? dataFactory.CreateDataJson(rate)));
        }

        var document = await reportsClient.GenerateGroupedPricingQuoteAsync(
            first.BaseQuote, options, cancellationToken);
        return new GroupCommercialQuoteResult(
            rateId, first.ParentId, first.BaseQuote, document);
    }
}

public sealed record GroupCommercialQuoteResult(
    Guid EffectiveRateId,
    Guid ParentRateId,
    string ParentQuoteNumber,
    GeneratedRateDocumentDto? Document);

public sealed class GroupCommercialQuoteApprovalException(string message)
    : InvalidOperationException(message);

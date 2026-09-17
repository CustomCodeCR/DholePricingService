using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Services;

public sealed class ImportRateAiFeedbackStore(ServiceDbContext dbContext)
    : IImportRateAiFeedbackStore
{
    private static readonly HashSet<string> AllowedOutcomes = new(StringComparer.OrdinalIgnoreCase)
    {
        "verified",
        "corrected",
        "rejected",
    };

    public async Task SaveAsync(
        ImportRateAiFeedback feedback,
        CancellationToken cancellationToken = default
    )
    {
        if (!feedback.ConfirmedAgainstSource)
        {
            throw new InvalidOperationException(
                "Debe confirmar que la tarifa fue revisada contra el correo o archivo original."
            );
        }

        var outcome = feedback.Outcome.Trim().ToLowerInvariant();
        if (!AllowedOutcomes.Contains(outcome))
        {
            throw new InvalidOperationException("El resultado del feedback de IA no es válido.");
        }

        var reasonCodes = feedback.ReasonCodes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (outcome == "rejected" && reasonCodes.Length == 0)
        {
            throw new InvalidOperationException(
                "Para rechazar una extracción debe indicar al menos un motivo estructurado."
            );
        }

        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pricing."ImportRateAiFeedback"
            (
                import_rate_id,
                confirmed_against_source,
                outcome,
                reason_codes_json,
                comment,
                correct_value,
                original_snapshot_json,
                reviewed_snapshot_json,
                reviewed_by,
                reviewed_at_utc,
                updated_at_utc
            )
            VALUES
            (
                @rate_id,
                TRUE,
                @outcome,
                CAST(@reason_codes_json AS jsonb),
                @comment,
                @correct_value,
                CAST(@original_snapshot_json AS jsonb),
                CAST(@reviewed_snapshot_json AS jsonb),
                @reviewed_by,
                @reviewed_at_utc,
                NOW()
            )
            ON CONFLICT (import_rate_id) DO UPDATE SET
                confirmed_against_source = TRUE,
                outcome = EXCLUDED.outcome,
                reason_codes_json = EXCLUDED.reason_codes_json,
                comment = EXCLUDED.comment,
                correct_value = EXCLUDED.correct_value,
                original_snapshot_json = CASE
                    WHEN pricing."ImportRateAiFeedback".original_snapshot_json = '{}'::jsonb
                        THEN EXCLUDED.original_snapshot_json
                    ELSE pricing."ImportRateAiFeedback".original_snapshot_json
                END,
                reviewed_snapshot_json = EXCLUDED.reviewed_snapshot_json,
                reviewed_by = EXCLUDED.reviewed_by,
                reviewed_at_utc = EXCLUDED.reviewed_at_utc,
                updated_at_utc = NOW();
            """;

        Add(command, "rate_id", feedback.ImportRateId);
        Add(command, "outcome", outcome);
        Add(command, "reason_codes_json", JsonSerializer.Serialize(reasonCodes));
        Add(command, "comment", Normalize(feedback.Comment));
        Add(command, "correct_value", Normalize(feedback.CorrectValue));
        Add(command, "original_snapshot_json", EnsureJson(feedback.OriginalSnapshotJson));
        Add(command, "reviewed_snapshot_json", EnsureJson(feedback.ReviewedSnapshotJson));
        Add(command, "reviewed_by", feedback.ReviewedBy);
        Add(command, "reviewed_at_utc", feedback.ReviewedAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> GetMissingReviewIdsAsync(
        IReadOnlyCollection<Guid> importRateIds,
        CancellationToken cancellationToken = default
    )
    {
        if (importRateIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var ids = importRateIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var aiRateIds = await dbContext.ImportFclRates
            .AsNoTracking()
            .Where(rate => ids.Contains(rate.Id) && rate.SourceType != ImportSourceType.Manual)
            .Select(rate => rate.Id)
            .ToArrayAsync(cancellationToken);

        if (aiRateIds.Length == 0)
        {
            return Array.Empty<Guid>();
        }

        var reviewed = await LoadReviewedIdsAsync(aiRateIds, cancellationToken);
        return aiRateIds.Where(id => !reviewed.Contains(id)).ToArray();
    }

    public async Task<IReadOnlyCollection<PricingLearningFeedback>> GetLearningContextAsync(
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        var take = Math.Clamp(limit, 1, 50);
        var feedbackRows = await LoadFeedbackRowsAsync(take, cancellationToken);
        if (feedbackRows.Count == 0)
        {
            return Array.Empty<PricingLearningFeedback>();
        }

        var ids = feedbackRows.Select(row => row.ImportRateId).Distinct().ToArray();
        var rates = await dbContext.ImportFclRates
            .AsNoTracking()
            .Where(rate => ids.Contains(rate.Id))
            .ToDictionaryAsync(rate => rate.Id, cancellationToken);

        return feedbackRows
            .Where(row => rates.ContainsKey(row.ImportRateId))
            .Select(row =>
            {
                var rate = rates[row.ImportRateId];
                return new PricingLearningFeedback(
                    rate.Id,
                    row.Outcome,
                    row.ReasonCodes,
                    row.Comment,
                    row.CorrectValue,
                    row.OriginalSnapshotJson,
                    row.ReviewedSnapshotJson,
                    rate.PolName,
                    rate.PoeName,
                    rate.PodName,
                    rate.ContainerTypeName,
                    rate.CarrierName,
                    rate.CurrencyCode,
                    rate.OceanFreight,
                    rate.OriginCharges,
                    rate.DestinationCharges,
                    rate.Surcharges,
                    rate.TotalCost,
                    rate.FreeDays,
                    rate.TransitDays,
                    rate.ValidFrom,
                    rate.ValidTo,
                    rate.Commodity,
                    rate.SpaceComment
                );
            })
            .ToArray();
    }

    private async Task<HashSet<Guid>> LoadReviewedIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken
    )
    {
        var result = new HashSet<Guid>();
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT import_rate_id
            FROM pricing."ImportRateAiFeedback"
            WHERE confirmed_against_source = TRUE
              AND outcome IN ('verified', 'corrected')
              AND import_rate_id IN ({string.Join(",", ids.Select((_, index) => $"@id{index}"))});
            """;
        var index = 0;
        foreach (var id in ids)
        {
            Add(command, $"id{index++}", id);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetGuid(0));
        }

        return result;
    }

    private async Task<List<FeedbackRow>> LoadFeedbackRowsAsync(
        int limit,
        CancellationToken cancellationToken
    )
    {
        var result = new List<FeedbackRow>();
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT import_rate_id,
                   outcome,
                   reason_codes_json::text,
                   comment,
                   correct_value,
                   original_snapshot_json::text,
                   reviewed_snapshot_json::text
            FROM pricing."ImportRateAiFeedback"
            WHERE confirmed_against_source = TRUE
            ORDER BY reviewed_at_utc DESC
            LIMIT @take;
            """;
        Add(command, "take", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var reasonJson = reader.IsDBNull(2) ? "[]" : reader.GetString(2);
            string[] reasons;
            try
            {
                reasons = JsonSerializer.Deserialize<string[]>(reasonJson) ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                reasons = Array.Empty<string>();
            }

            result.Add(new FeedbackRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reasons,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? "{}" : reader.GetString(5),
                reader.IsDBNull(6) ? "{}" : reader.GetString(6)
            ));
        }

        return result;
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string EnsureJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "{}";
        try
        {
            using var _ = JsonDocument.Parse(value);
            return value;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { value });
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record FeedbackRow(
        Guid ImportRateId,
        string Outcome,
        IReadOnlyCollection<string> ReasonCodes,
        string? Comment,
        string? CorrectValue,
        string OriginalSnapshotJson,
        string ReviewedSnapshotJson
    );
}

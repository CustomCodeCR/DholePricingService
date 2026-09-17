using System.Text.Json;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Imports.RejectImportRate;

public sealed class RejectImportRateCommandHandler(
    IImportFclRateRepository importRates,
    IImportRateAiFeedbackStore aiFeedback,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<RejectImportRateCommand, Result>
{
    public async Task<Result> HandleAsync(
        RejectImportRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var ids = command.Ids.Where(x => x != Guid.Empty).Distinct().ToArray();
        var feedback = ParseFeedback(command.Reason);

        if (ids.Length == 0)
        {
            return Result.Failure(PricingErrors.InvalidImportFclRate);
        }

        if (
            feedback is null
            || !feedback.ConfirmedAgainstSource
            || feedback.ReasonCodes.Count == 0
        )
        {
            return Result.Failure(PricingErrors.ImportFclRateRejectReasonIsRequired);
        }

        var entities = new List<ImportFclRates>(ids.Length);

        foreach (var id in ids)
        {
            var importRate = await importRates.GetByIdAsync(id, cancellationToken);

            if (importRate is null || importRate.IsDeleted)
            {
                return Result.Failure(PricingErrors.ImportFclRateNotFound);
            }

            importRate.ExpireIfNeeded(DateTime.UtcNow.Date, command.RejectedBy);

            if (importRate.Status == ImportStatus.Expired)
            {
                return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
            }

            if (importRate.Status == ImportStatus.Rejected)
            {
                entities.Add(importRate);
                continue;
            }

            if (!importRate.CanBeRejected)
            {
                return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
            }

            entities.Add(importRate);
        }

        var rejectableEntities = entities
            .Where(importRate => importRate.CanBeRejected)
            .ToArray();

        foreach (var importRate in rejectableEntities)
        {
            var before = PricingAuditSnapshots.From(importRate);
            var snapshotJson = JsonSerializer.Serialize(before);

            importRate.Reject(command.RejectedBy);

            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.ImportFclRateRejected,
                    Action: PricingAuditActions.Rejected,
                    EntityType: PricingAuditEntityTypes.ImportFclRate,
                    EntityId: importRate.Id,
                    ActorUserId: command.RejectedBy,
                    Before: before,
                    After: PricingAuditSnapshots.From(importRate),
                    Payload: new
                    {
                        importRate.Id,
                        importRate.ImportBatchId,
                        FeedbackOutcome = "rejected",
                        feedback.ReasonCodes,
                        feedback.Comment,
                        feedback.CorrectValue,
                        Status = importRate.Status.ToString(),
                    }
                ),
                cancellationToken
            );

            await aiFeedback.SaveAsync(
                new ImportRateAiFeedback(
                    importRate.Id,
                    ConfirmedAgainstSource: true,
                    Outcome: "rejected",
                    ReasonCodes: feedback.ReasonCodes,
                    Comment: feedback.Comment,
                    CorrectValue: feedback.CorrectValue,
                    OriginalSnapshotJson: snapshotJson,
                    ReviewedSnapshotJson: snapshotJson,
                    ReviewedBy: command.RejectedBy,
                    ReviewedAtUtc: DateTime.UtcNow
                ),
                cancellationToken
            );
        }

        if (rejectableEntities.Length == 0)
        {
            return Result.Success();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var importRate in rejectableEntities)
        {
            await cache.RemoveImportRateCacheAsync(
                importRate.Id,
                importRate.ImportBatchId,
                cancellationToken
            );
        }

        return Result.Success();
    }

    private static StructuredFeedback? ParseFeedback(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var confirmed = root.TryGetProperty("confirmedAgainstSource", out var confirmedNode)
                && confirmedNode.ValueKind == JsonValueKind.True;
            var reasonCodes = root.TryGetProperty("reasonCodes", out var reasonsNode)
                && reasonsNode.ValueKind == JsonValueKind.Array
                    ? reasonsNode.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()?.Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : Array.Empty<string>();
            var comment = ReadText(root, "comment");
            var correctValue = ReadText(root, "correctValue");

            return new StructuredFeedback(confirmed, reasonCodes, comment, correctValue);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadText(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.String)
            return null;
        var value = node.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record StructuredFeedback(
        bool ConfirmedAgainstSource,
        IReadOnlyCollection<string> ReasonCodes,
        string? Comment,
        string? CorrectValue
    );
}

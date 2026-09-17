namespace Dhole.Pricing.Application.Abstractions.Services;

public interface IImportRateAiFeedbackStore
{
    Task SaveAsync(
        ImportRateAiFeedback feedback,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<Guid>> GetMissingReviewIdsAsync(
        IReadOnlyCollection<Guid> importRateIds,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<PricingLearningFeedback>> GetLearningContextAsync(
        int limit,
        CancellationToken cancellationToken = default
    );
}

public sealed record ImportRateAiFeedback(
    Guid ImportRateId,
    bool ConfirmedAgainstSource,
    string Outcome,
    IReadOnlyCollection<string> ReasonCodes,
    string? Comment,
    string? CorrectValue,
    string OriginalSnapshotJson,
    string ReviewedSnapshotJson,
    Guid? ReviewedBy,
    DateTime ReviewedAtUtc
);

public sealed record PricingLearningFeedback(
    Guid ImportRateId,
    string Outcome,
    IReadOnlyCollection<string> ReasonCodes,
    string? Comment,
    string? CorrectValue,
    string OriginalSnapshotJson,
    string ReviewedSnapshotJson,
    string? Pol,
    string? Poe,
    string? Pod,
    string? ContainerType,
    string? Carrier,
    string? Currency,
    decimal? OceanFreight,
    decimal? OriginCharges,
    decimal? DestinationCharges,
    decimal? Surcharges,
    decimal? TotalCost,
    int FreeDays,
    int? TransitDays,
    DateTime ValidFrom,
    DateTime ValidTo,
    string? Commodity,
    string? SpaceComment
);

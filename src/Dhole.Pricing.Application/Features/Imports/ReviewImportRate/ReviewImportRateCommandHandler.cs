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
using Dhole.Pricing.Domain.Imports.Services;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Imports.ReviewImportRate;

public sealed class ReviewImportRateCommandHandler(
    IImportFclRateRepository importRates,
    IPricingConfigCatalogClient configCatalog,
    IImportRateAiFeedbackStore aiFeedback,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<ReviewImportRateCommand, Result>
{
    public async Task<Result> HandleAsync(
        ReviewImportRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var importRate = await importRates.GetByIdAsync(command.ImportRateId, cancellationToken);
        if (importRate is null || importRate.IsDeleted)
        {
            return Result.Failure(PricingErrors.ImportFclRateNotFound);
        }

        if (!importRate.CanBeManuallyReviewed)
        {
            return Result.Failure(PricingErrors.ImportFclRateInvalidStatus);
        }

        if (
            command.OceanFreight < 0m
            || command.OriginCharges < 0m
            || command.DestinationCharges < 0m
            || command.Surcharges < 0m
            || command.TotalSale < 0m
            || command.FreeDays < 0
            || command.TransitDays < 0
            || command.ValidTo < command.ValidFrom
        )
        {
            return Result.Failure(PricingErrors.InvalidImportFclRate);
        }

        var profile = await ResolveAsync(command.ImportProfileId, ["pricing-imports-profiles"], cancellationToken);
        var pol = await ResolveAsync(command.PolId, ["pol", "ports"], cancellationToken);
        var poe = await ResolveAsync(command.PoeId, ["poe", "ports"], cancellationToken);
        PricingConfigCatalogItem? pod = null;
        if (command.PodId.HasValue)
        {
            pod = await ResolveAsync(command.PodId.Value, ["pod", "ports"], cancellationToken);
        }
        var shipmentMode = ResolveShipmentMode(command.ShipmentMode, importRate);
        var isConsolidatedImport =
            shipmentMode is ImportedShipmentMode.Lcl or ImportedShipmentMode.Air;
        var carrierWasProvided = command.CarrierId.HasValue && command.CarrierId.Value != Guid.Empty;
        var containerTypeWasProvided = !isConsolidatedImport
            && command.ContainerTypeId.HasValue
            && command.ContainerTypeId.Value != Guid.Empty;

        var carrier = carrierWasProvided
            ? await ResolveAsync(command.CarrierId!.Value, ["carriers"], cancellationToken)
            : null;
        var agent = await ResolveAsync(command.AgentId, ["agents"], cancellationToken);
        var containerType = containerTypeWasProvided
            ? await ResolveAsync(command.ContainerTypeId!.Value, ["container-types", "containers-types"], cancellationToken)
            : null;
        var currency = await ResolveAsync(command.CurrencyId, ["currencies"], cancellationToken);

        if (
            profile is null
            || pol is null
            || poe is null
            || (command.PodId.HasValue && pod is null)
            || agent is null
            || currency is null
            || (!isConsolidatedImport && (carrier is null || containerType is null))
            || (carrierWasProvided && carrier is null)
            || (containerTypeWasProvided && containerType is null)
        )
        {
            return Result.Failure(PricingErrors.ImportFclRateCatalogConcordanceRequired);
        }

        var carrierSnapshot = carrier is null
            ? new CatalogSnapshot(
                importRate.CarrierId,
                importRate.CarrierName,
                importRate.CarrierCode,
                importRate.CarrierSlug
            )
            : Snapshot(carrier);
        var containerTypeSnapshot = shipmentMode switch
        {
            ImportedShipmentMode.Lcl => LclContainerSnapshot(),
            ImportedShipmentMode.Air => AirContainerSnapshot(),
            _ => containerType is null
                ? new CatalogSnapshot(
                    importRate.ContainerTypeId,
                    importRate.ContainerTypeName,
                    importRate.ContainerTypeCode,
                    importRate.ContainerTypeSlug
                )
                : Snapshot(containerType),
        };

        var before = PricingAuditSnapshots.From(importRate);
        var beforeJson = JsonSerializer.Serialize(before);
        var podSnapshot = pod is null
            ? new CatalogSnapshot(importRate.PodId, importRate.PodName, importRate.PodCode, importRate.PodSlug)
            : Snapshot(pod);

        try
        {
            importRate.ApplyManualReview(
                Snapshot(profile),
                Snapshot(pol),
                Snapshot(poe),
                podSnapshot,
                carrierSnapshot,
                Snapshot(agent),
                containerTypeSnapshot,
                Snapshot(currency),
                command.Commodity,
                command.SpaceComment,
                command.OceanFreight,
                command.OriginCharges,
                command.DestinationCharges,
                command.Surcharges,
                command.TotalSale,
                command.FreeDays,
                command.TransitDays,
                command.ValidFrom,
                command.ValidTo,
                command.UpdatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(PricingErrors.InvalidImportFclRate);
        }

        var after = PricingAuditSnapshots.From(importRate);
        var afterJson = JsonSerializer.Serialize(after);
        var corrected = !string.Equals(beforeJson, afterJson, StringComparison.Ordinal);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.ImportFclRateUpdated,
                Action: PricingAuditActions.Updated,
                EntityType: PricingAuditEntityTypes.ImportFclRate,
                EntityId: importRate.Id,
                ActorUserId: command.UpdatedBy,
                Before: before,
                After: after,
                Payload: new
                {
                    importRate.Id,
                    importRate.ImportBatchId,
                    ReviewApplied = true,
                    HumanFeedbackOutcome = corrected ? "corrected" : "verified",
                    command.ReviewNotes,
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await aiFeedback.SaveAsync(
            new ImportRateAiFeedback(
                importRate.Id,
                ConfirmedAgainstSource: true,
                Outcome: corrected ? "corrected" : "verified",
                ReasonCodes: corrected ? ["human_correction"] : Array.Empty<string>(),
                Comment: command.ReviewNotes,
                CorrectValue: null,
                OriginalSnapshotJson: beforeJson,
                ReviewedSnapshotJson: afterJson,
                ReviewedBy: command.UpdatedBy,
                ReviewedAtUtc: DateTime.UtcNow
            ),
            cancellationToken
        );

        await cache.RemoveImportRateCacheAsync(
            importRate.Id,
            importRate.ImportBatchId,
            cancellationToken
        );

        return Result.Success();
    }

    private async Task<PricingConfigCatalogItem?> ResolveAsync(
        Guid id,
        IReadOnlyCollection<string> acceptedGroups,
        CancellationToken cancellationToken
    )
    {
        if (id == Guid.Empty) return null;

        var item = await configCatalog.GetActiveByIdAsync(id, cancellationToken);
        return item is not null
            && acceptedGroups.Contains(item.CatalogGroupSlug, StringComparer.OrdinalIgnoreCase)
            ? item
            : null;
    }

    private static ImportedShipmentMode ResolveShipmentMode(
        string? requested,
        ImportFclRates importRate)
    {
        var normalized = requested?.Trim();
        if (string.Equals(normalized, "Lcl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "LCL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "LclColoader", StringComparison.OrdinalIgnoreCase))
        {
            return ImportedShipmentMode.Lcl;
        }

        if (string.Equals(normalized, "Air", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "AirConsol", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "AirLclColoader", StringComparison.OrdinalIgnoreCase))
        {
            return ImportedShipmentMode.Air;
        }

        if (string.Equals(normalized, "Fcl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "FCL", StringComparison.OrdinalIgnoreCase))
        {
            return ImportedShipmentMode.Fcl;
        }

        return importRate.ShipmentMode != ImportedShipmentMode.Unknown
            ? importRate.ShipmentMode
            : ImportShipmentModeClassifier.Classify(
                importRate.ContainerType,
                importRate.ContainerTypeName,
                importRate.ContainerTypeCode,
                importRate.ContainerTypeSlug,
                importRate.RawDataJson
            );
    }

    private static CatalogSnapshot LclContainerSnapshot() =>
        new(Guid.Parse("f4d19764-7556-2a0d-9222-42d7b48d00d8"), "LCL", "LCL", "lcl");

    private static CatalogSnapshot AirContainerSnapshot() =>
        new(Guid.Parse("321ae516-76a1-10ed-6d98-2117496f8ff4"), "AIR", "AIR", "air");

    private static CatalogSnapshot Snapshot(PricingConfigCatalogItem item) =>
        CatalogSnapshot.Create(item.Id, item.Name, item.Code, item.Slug);
}

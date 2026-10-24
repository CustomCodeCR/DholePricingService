using System.Text.Json;
using CustomCodeFramework.Core.Domain.Entities;
using CustomCodeFramework.Messaging.Inbox;
using CustomCodeFramework.Messaging.Outbox;
using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using CustomCodeFramework.Postgres.EntityFramework.DbContexts;
using Dhole.Pricing.Domain.Competitors.Entities;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.Auditing;
using Dhole.Pricing.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.DbContexts;

public sealed class ServiceDbContext(DbContextOptions<ServiceDbContext> options)
    : AppDbContextBase(options)
{
    private const string SourceService = "DholePricingService";

    public DbSet<CompetitorAnalysisDocument> CompetitorAnalysisDocuments => Set<CompetitorAnalysisDocument>();

    public DbSet<CompetitorTariff> CompetitorTariffs => Set<CompetitorTariff>();

    public DbSet<CompetitorTariffImportPayload> CompetitorTariffImportPayloads => Set<CompetitorTariffImportPayload>();

    public DbSet<CompetitorRateObservation> CompetitorRateObservations => Set<CompetitorRateObservation>();

    public DbSet<PricingMarketDecision> PricingMarketDecisions => Set<PricingMarketDecision>();

    public DbSet<PricingMarketDecisionObservation> PricingMarketDecisionObservations =>
        Set<PricingMarketDecisionObservation>();

    public DbSet<AutoPricingProfile> AutoPricingProfiles => Set<AutoPricingProfile>();

    public DbSet<ChargePricingRule> ChargePricingRules => Set<ChargePricingRule>();

    public DbSet<Cost> Costs => Set<Cost>();
    public DbSet<CostIncoterm> CostIncoterms => Set<CostIncoterm>();

    public DbSet<ImportFclRates> ImportFclRates => Set<ImportFclRates>();

    public DbSet<ManualImportExtractionJob> ManualImportExtractionJobs => Set<ManualImportExtractionJob>();

    public DbSet<PricingImportFromExtractionJob> PricingImportFromExtractionJobs =>
        Set<PricingImportFromExtractionJob>();

    public DbSet<RateHeader> RateHeaders => Set<RateHeader>();

    public DbSet<RateDetail> RateDetails => Set<RateDetail>();

    public DbSet<RateContainerAllocation> RateContainerAllocations => Set<RateContainerAllocation>();

    public DbSet<RateTermItem> RateTermItems => Set<RateTermItem>();

    public DbSet<RateRequest> RateRequests => Set<RateRequest>();

    public DbSet<RateComparison> RateComparisons => Set<RateComparison>();

    public DbSet<RateComparisonDetail> RateComparisonDetails => Set<RateComparisonDetail>();

    public DbSet<DynamicsQuoteSyncJob> DynamicsQuoteSyncJobs => Set<DynamicsQuoteSyncJob>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AddDomainEventsToOutbox();

        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        AddDomainEventsToOutbox();

        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pricing");

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServiceDbContext).Assembly);

        modelBuilder.Entity<DynamicsQuoteSyncJob>(entity =>
        {
            // Legacy migration intentionally creates quoted PascalCase columns.
            // Pin each column name: global snake_case convention must not rename
            // the physical schema used by the durable delivery worker.
            entity.ToTable("DynamicsQuoteSyncJobs");
            entity.HasKey(x => x.Id).HasName("DynamicsQuoteSyncJobs_pkey");
            entity.Property(x => x.Id).ValueGeneratedNever().HasColumnName("Id");
            entity.Property(x => x.RateId).HasColumnName("RateId");
            entity.Property(x => x.RevisionNumber).HasColumnName("RevisionNumber");
            entity.Property(x => x.EventId).HasColumnName("EventId");
            entity.Property(x => x.PayloadJson).HasColumnName("PayloadJson");
            entity.Property(x => x.Status).HasColumnName("Status").HasMaxLength(32);
            entity.Property(x => x.Attempts).HasColumnName("Attempts");
            entity.Property(x => x.RecoveryAttempt).HasColumnName("RecoveryAttempt").HasDefaultValue(0);
            entity.Property(x => x.RecoveryReason).HasColumnName("RecoveryReason").HasMaxLength(500);
            entity.Property(x => x.RecoveryApprovedBy).HasColumnName("RecoveryApprovedBy").HasMaxLength(128);
            entity.Property(x => x.RecoveryApprovedAtUtc).HasColumnName("RecoveryApprovedAtUtc");
            entity.Property(x => x.NextAttemptAtUtc).HasColumnName("NextAttemptAtUtc");
            entity.Property(x => x.LeaseExpiresAtUtc).HasColumnName("LeaseExpiresAtUtc");
            entity.Property(x => x.LastError).HasColumnName("LastError").HasMaxLength(500);
            entity.Property(x => x.ResponseJson).HasColumnName("ResponseJson");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("CreatedAtUtc");
            entity.Property(x => x.CompletedAtUtc).HasColumnName("CompletedAtUtc");
            entity.HasIndex(x => new { x.RateId, x.RevisionNumber })
                .IsUnique().HasDatabaseName("UX_DynamicsQuoteSyncJobs_RateRevision");
            entity.HasIndex(x => x.EventId)
                .IsUnique().HasDatabaseName("UX_DynamicsQuoteSyncJobs_Event");
        });

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
    }

    private void AddDomainEventsToOutbox()
    {
        var aggregateRoots = ChangeTracker
            .Entries()
            .Select(x => x.Entity)
            .OfType<AggregateRoot<Guid>>()
            .Where(x => x.DomainEvents.Count > 0)
            .ToList();

        if (aggregateRoots.Count == 0)
        {
            return;
        }

        var outboxMessages = new List<OutboxMessage>();

        foreach (var aggregateRoot in aggregateRoots)
        {
            foreach (var domainEvent in aggregateRoot.DomainEvents)
            {
                var eventType = DomainEventOutboxMapper.GetEventType(domainEvent);
                var eventName = DomainEventOutboxMapper.GetEventName(domainEvent);

                var payloadJson = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
                var correlationId =
                    AuditExecutionContextAccessor.Current?.CorrelationId ?? Guid.NewGuid();

                outboxMessages.Add(
                    new OutboxMessage
                    {
                        EventId = domainEvent.EventId,
                        EventType = eventType,
                        EventName = eventName,
                        SourceService = SourceService,
                        PayloadJson = payloadJson,
                        HeadersJson = null,
                        CorrelationId = correlationId.ToString(),
                        Status = OutboxMessageStatus.Pending,
                        RetryCount = 0,
                        ErrorMessage = null,
                        CreatedAtUtc = DateTime.UtcNow,
                    }
                );
            }

            aggregateRoot.ClearDomainEvents();
        }

        OutboxMessages.AddRange(outboxMessages);
    }
}

using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.Imports.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Imports;

internal sealed class PricingImportFromExtractionJobConfiguration
    : EntityTypeConfigurationBase<PricingImportFromExtractionJob, Guid>
{
    public override void Configure(
        EntityTypeBuilder<PricingImportFromExtractionJob> builder
    )
    {
        base.Configure(builder);

        builder.ToTable("PricingImportFromExtractionJobs");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ExternalRequestId).IsRequired();
        builder.Property(x => x.EmailExtractionJobId).IsRequired();
        builder.Property(x => x.ExtractionExecutionId).IsRequired();
        builder.Property(x => x.PricingImportId).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxAttemptCount).HasDefaultValue(3).IsRequired();
        builder.Property(x => x.NextAttemptAtUtc);
        builder.Property(x => x.LeaseOwner).HasMaxLength(250);
        builder.Property(x => x.LeaseExpiresAtUtc);
        builder.Property(x => x.ErrorCode).HasMaxLength(250);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.PersistedRows).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.SkippedRows).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.StartedAtUtc);
        builder.Property(x => x.CompletedAtUtc);
        builder.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();

        builder.HasIndex(x => x.ExternalRequestId).IsUnique();
        builder.HasIndex(x => x.EmailExtractionJobId);
        builder.HasIndex(x => x.ExtractionExecutionId);
        builder.HasIndex(x => x.PricingImportId);
        builder
            .HasIndex(x => new
            {
                x.Status,
                x.NextAttemptAtUtc,
                x.CreatedAtUtc,
            })
            .HasDatabaseName("ix_pricing_extraction_jobs_queue");
        builder
            .HasIndex(x => new
            {
                x.Status,
                x.LeaseExpiresAtUtc,
            })
            .HasDatabaseName("ix_pricing_extraction_jobs_lease");
    }
}

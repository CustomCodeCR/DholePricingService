using Dhole.Pricing.Domain.Imports.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Imports;

internal sealed class ManualImportExtractionJobConfiguration : IEntityTypeConfiguration<ManualImportExtractionJob>
{
    public void Configure(EntityTypeBuilder<ManualImportExtractionJob> builder)
    {
        builder.ToTable("ManualImportExtractionJobs", "pricing");
        builder.HasKey(x => x.Id).HasName("pk_manual_import_extraction_jobs");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RequestedBy).IsRequired();
        builder.Property(x => x.RequestedByName).HasMaxLength(300);
        builder.Property(x => x.ClientRequestKey).HasMaxLength(150);
        builder.Property(x => x.OriginalFileName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150);
        builder.Property(x => x.ProfileSlug).HasMaxLength(150);
        builder.Property(x => x.CorrelationId).HasMaxLength(150).IsRequired();
        builder.Property(x => x.FileContent).HasColumnType("bytea").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ResultJson).HasColumnType("jsonb");
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.HasIndex(x => new { x.RequestedBy, x.ClientRequestKey })
            .IsUnique()
            .HasFilter("client_request_key IS NOT NULL")
            .HasDatabaseName("ix_manual_import_jobs_request_key");
        builder.HasIndex(x => new { x.Status, x.LeaseExpiresAtUtc, x.CreatedAtUtc })
            .HasDatabaseName("ix_manual_import_jobs_queue");
    }
}

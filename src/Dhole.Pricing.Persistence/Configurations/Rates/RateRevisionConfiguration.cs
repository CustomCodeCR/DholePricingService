using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateRevisionConfiguration : IEntityTypeConfiguration<RateRevision>
{
    public void Configure(EntityTypeBuilder<RateRevision> builder)
    {
        builder.ToTable("RateRevisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RateHeaderId).IsRequired();
        builder.Property(x => x.RevisionNumber).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RateName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.IdtraNumber).HasMaxLength(100).IsRequired(false);
        builder.Property(x => x.QuoNumber).HasMaxLength(100).IsRequired(false);
        builder.Property(x => x.TotalSaleUsd).HasPrecision(18,2).IsRequired();
        builder.Property(x => x.TotalSaleCrc).HasPrecision(18,2).IsRequired();
        builder.Property(x => x.MarginPercentage).HasPrecision(18,4).IsRequired();
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).IsRequired(false);
        builder.HasOne<RateHeader>().WithMany().HasForeignKey(x => x.RateHeaderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.RateHeaderId, x.RevisionNumber }).IsUnique().HasDatabaseName("ux_rate_revisions_header_number");
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}

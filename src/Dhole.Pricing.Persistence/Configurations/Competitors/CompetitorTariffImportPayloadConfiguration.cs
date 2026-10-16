using Dhole.Pricing.Domain.Competitors.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Competitors;

internal sealed class CompetitorTariffImportPayloadConfiguration
    : IEntityTypeConfiguration<CompetitorTariffImportPayload>
{
    public void Configure(EntityTypeBuilder<CompetitorTariffImportPayload> builder)
    {
        builder.ToTable("CompetitorTariffImportPayloads");
        builder.HasKey(x => x.CompetitorTariffId).HasName("CompetitorTariffImportPayloads_pkey");
        builder.Property(x => x.CompetitorTariffId).ValueGeneratedNever().HasColumnName("CompetitorTariffId");
        builder.Property(x => x.FileContent).HasColumnType("bytea").IsRequired().HasColumnName("FileContent");
        builder.Property(x => x.FileHash).HasMaxLength(64).IsRequired().HasColumnName("FileHash");
        builder.Property(x => x.ContentType).HasMaxLength(150).HasColumnName("ContentType");
        builder.Property(x => x.RequestedBy).HasColumnName("RequestedBy");
        builder.Property(x => x.AttemptCount).HasColumnName("AttemptCount");
        builder.Property(x => x.RequestedByName).HasMaxLength(250).HasColumnName("RequestedByName");
        builder.Property(x => x.NextAttemptAtUtc).HasColumnType("timestamp with time zone").HasColumnName("NextAttemptAtUtc");
        builder.Property(x => x.LeaseUntilUtc).HasColumnType("timestamp with time zone").HasColumnName("LeaseUntilUtc");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("CreatedAtUtc");
        builder.Property(x => x.FallbackFrom).HasColumnType("timestamp with time zone").HasColumnName("FallbackFrom");
        builder.Property(x => x.FallbackTo).HasColumnType("timestamp with time zone").HasColumnName("FallbackTo");
        builder.HasIndex(x => new { x.NextAttemptAtUtc, x.LeaseUntilUtc })
            .HasDatabaseName("IX_CompetitorPayloads_NextAttempt_Lease");
        builder.HasOne<CompetitorTariff>()
            .WithOne()
            .HasForeignKey<CompetitorTariffImportPayload>(x => x.CompetitorTariffId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("CompetitorTariffImportPayloads_CompetitorTariffId_fkey");
    }
}

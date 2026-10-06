using Dhole.Pricing.Domain.Competitors.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Competitors;

internal sealed class CompetitorTariffConfiguration : IEntityTypeConfiguration<CompetitorTariff>
{
    public void Configure(EntityTypeBuilder<CompetitorTariff> builder)
    {
        builder.ToTable("CompetitorTariffs");

        builder.HasKey(x => x.Id).HasName("PK_CompetitorTariffs");

        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("Id");
        builder.Property(x => x.PolIds).HasColumnType("uuid[]").HasColumnName("PolIds").IsRequired();
        builder.Property(x => x.PoeIds).HasColumnType("uuid[]").HasColumnName("PoeIds").IsRequired();
        builder.Property(x => x.PodIds).HasColumnType("uuid[]").HasColumnName("PodIds").IsRequired();
        builder.Property(x => x.CarrierIds).HasColumnType("uuid[]").HasColumnName("CarrierIds").IsRequired();
        builder.Property(x => x.ValidFrom).HasColumnType("timestamp with time zone").HasColumnName("ValidFrom");
        builder.Property(x => x.ValidTo).HasColumnType("timestamp with time zone").HasColumnName("ValidTo");
        builder
            .Property(x => x.ShipmentMode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasColumnName("ShipmentMode")
            .IsRequired();
        builder.Property(x => x.StorageId).HasColumnName("StorageId").IsRequired();

        builder
            .Property(x => x.CompetitorCompanyName)
            .HasMaxLength(250)
            .HasColumnName("CompetitorCompanyName")
            .IsRequired();
        builder.Property(x => x.IncotermId).HasColumnName("IncotermId");
        builder.Property(x => x.OriginalFileName).HasMaxLength(500).HasColumnName("OriginalFileName");
        builder.Property(x => x.ExtractionExecutionId).HasColumnName("ExtractionExecutionId");
        builder.Property(x => x.ObservationCount).HasColumnName("ObservationCount").IsRequired();
        builder.Property(x => x.ReviewCount).HasColumnName("ReviewCount").IsRequired();
        builder.Property(x => x.ImportStatus).HasMaxLength(40).HasColumnName("ImportStatus").IsRequired();
        builder
            .Property(x => x.ImportedAtUtc)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("ImportedAtUtc")
            .IsRequired();

        builder.HasIndex(x => x.ShipmentMode).HasDatabaseName("IX_CompetitorTariffs_ShipmentMode");
        builder.HasIndex(x => x.ValidTo).HasDatabaseName("IX_CompetitorTariffs_ValidTo");
        builder.HasIndex(x => x.CompetitorCompanyName).HasDatabaseName("IX_CompetitorTariffs_CompetitorCompanyName");
        builder.HasIndex(x => x.ImportStatus).HasDatabaseName("IX_CompetitorTariffs_ImportStatus");
    }
}

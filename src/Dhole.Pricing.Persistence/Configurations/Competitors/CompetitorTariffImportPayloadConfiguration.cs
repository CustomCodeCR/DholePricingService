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
        builder.HasKey(x => x.CompetitorTariffId);
        builder.Property(x => x.CompetitorTariffId).ValueGeneratedNever();
        builder.Property(x => x.FileContent).HasColumnType("bytea").IsRequired();
        builder.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150);
        builder.Property(x => x.RequestedByName).HasMaxLength(250);
        builder.Property(x => x.NextAttemptAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.LeaseUntilUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.FallbackFrom).HasColumnType("timestamp with time zone");
        builder.Property(x => x.FallbackTo).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.NextAttemptAtUtc, x.LeaseUntilUtc });
        builder.HasOne<CompetitorTariff>()
            .WithOne()
            .HasForeignKey<CompetitorTariffImportPayload>(x => x.CompetitorTariffId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

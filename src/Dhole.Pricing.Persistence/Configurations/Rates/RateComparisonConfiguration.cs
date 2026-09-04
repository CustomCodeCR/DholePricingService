using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateComparisonConfiguration : EntityTypeConfigurationBase<RateComparison, Guid>
{
    public override void Configure(EntityTypeBuilder<RateComparison> builder)
    {
        base.Configure(builder);
        builder.ToTable("RateComparisons");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ComparedRateCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ComparisonType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.PolName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.PoeName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ContainerTypeName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.BaselineCostAmount).HasPrecision(18, 2);
        builder.Property(x => x.BaselineSaleAmount).HasPrecision(18, 2);
        builder.Property(x => x.CandidateCostAmount).HasPrecision(18, 2);
        builder.Property(x => x.CandidateSaleAmount).HasPrecision(18, 2);
        builder.Property(x => x.BaselineComparedAmount).HasPrecision(18, 2);
        builder.Property(x => x.CandidateComparedAmount).HasPrecision(18, 2);
        builder.Property(x => x.SavingsAmount).HasPrecision(18, 2);
        builder.Property(x => x.SavingsPercent).HasPrecision(9, 4);
        builder.Property(x => x.CandidatePayloadJson).HasColumnType("jsonb").IsRequired();

        // The generated migration and model snapshot intentionally track these comparison snapshots.
        builder.HasMany(x => x.Details).WithOne().HasForeignKey(x => x.RateComparisonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.SourceImportFclRateId, x.ComparedRateHeaderId, x.ComparisonType }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.PolId, x.PoeId, x.ContainerTypeId });
    }
}
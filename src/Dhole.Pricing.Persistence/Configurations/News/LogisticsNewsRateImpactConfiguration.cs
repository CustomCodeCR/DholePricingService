using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.News.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.News;

internal sealed class LogisticsNewsRateImpactConfiguration
    : EntityTypeConfigurationBase<LogisticsNewsRateImpact, Guid>
{
    public override void Configure(EntityTypeBuilder<LogisticsNewsRateImpact> builder)
    {
        base.Configure(builder);

        builder.ToTable("LogisticsNewsRateImpacts");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.LogisticsNewsId).IsRequired();
        builder.Property(x => x.ImportFclRateId).IsRequired();
        builder.Property(x => x.MatchReason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Confidence).HasPrecision(5, 4).IsRequired();
        builder.Property(x => x.AppliedComment).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.AppliedAtUtc).IsRequired();

        builder
            .HasIndex(x => new { x.LogisticsNewsId, x.ImportFclRateId })
            .IsUnique()
            .HasDatabaseName("ux_logistics_news_rate_impacts_news_rate");
        builder.HasIndex(x => x.ImportFclRateId);
    }
}

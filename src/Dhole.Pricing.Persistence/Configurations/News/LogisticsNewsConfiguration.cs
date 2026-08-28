using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.News.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.News;

internal sealed class LogisticsNewsConfiguration
    : EntityTypeConfigurationBase<LogisticsNews, Guid>
{
    public override void Configure(EntityTypeBuilder<LogisticsNews> builder)
    {
        base.Configure(builder);

        builder.ToTable("LogisticsNews");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(6000).IsRequired();
        builder.Property(x => x.SourceCountry).HasMaxLength(120);
        builder.Property(x => x.SourceOffice).HasMaxLength(160);
        builder.Property(x => x.ReceivedAtUtc).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.AiSummary).HasMaxLength(1200);
        builder.Property(x => x.AiAnalysisJson).HasColumnType("jsonb");
        builder.Property(x => x.EventType).HasMaxLength(80);
        builder.Property(x => x.Severity).HasMaxLength(30);
        builder.Property(x => x.AiConfidence).HasPrecision(5, 4);
        builder.Property(x => x.MatchedRateCount).IsRequired();
        builder.Property(x => x.AppliedRateCount).IsRequired();
        builder.Property(x => x.LastProcessedAtUtc);
        builder.Property(x => x.ProcessingError).HasMaxLength(2000);

        builder.HasIndex(x => x.ReceivedAtUtc);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.IsActive);
    }
}

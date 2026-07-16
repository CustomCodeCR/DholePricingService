using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateHeaderConfiguration : EntityTypeConfigurationBase<RateHeader, Guid>
{
    public override void Configure(EntityTypeBuilder<RateHeader> builder)
    {
        base.Configure(builder);

        builder.ToTable("RateHeaders");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SourceImportFclRateId).IsRequired(false);

        builder.Ignore(x => x.ClientName);

        builder.Property(x => x.AgentId).IsRequired();

        builder.Property(x => x.AgentName).HasMaxLength(250).IsRequired();

        builder.Property(x => x.AgentCode).HasMaxLength(80).IsRequired();

        builder.Property(x => x.CarrierId).IsRequired();

        builder.Property(x => x.CarrierName).HasMaxLength(250).IsRequired();

        builder.Property(x => x.CarrierCode).HasMaxLength(80).IsRequired();

        builder.Property(x => x.PolId).IsRequired();

        builder.Property(x => x.PolName).HasMaxLength(250).IsRequired();

        builder.Property(x => x.PolCode).HasMaxLength(80).IsRequired();

        builder.Property(x => x.PoeId).IsRequired();

        builder.Property(x => x.PoeName).HasMaxLength(250).IsRequired();

        builder.Property(x => x.PoeCode).HasMaxLength(80).IsRequired();

        builder.Property(x => x.PodId).IsRequired();

        builder.Property(x => x.PodName).HasMaxLength(250).IsRequired();

        builder.Property(x => x.PodCode).HasMaxLength(80).IsRequired();

        builder.Property(x => x.ContainerTypeId).IsRequired();

        builder.Property(x => x.ContainerTypeName).HasMaxLength(120).IsRequired();

        builder.Property(x => x.ContainerTypeCode).HasMaxLength(80).IsRequired();

        builder.Property(x => x.CurrencyId).IsRequired();

        builder.Property(x => x.CurrencyName).HasMaxLength(120).IsRequired();

        builder.Property(x => x.CurrencyCode).HasMaxLength(20).IsRequired();

        builder.Property(x => x.FreeDays).IsRequired();

        builder.Property(x => x.ValidFrom).IsRequired();

        builder.Property(x => x.ValidTo).IsRequired();

        builder.Property(x => x.RateCode).HasMaxLength(10).IsRequired();

        builder.Property(x => x.RateName).HasMaxLength(500).IsRequired();

        builder.Property(x => x.ContainerQuantity).IsRequired().HasDefaultValue(1);

        builder.Property(x => x.TotalCostAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.TotalSaleAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.TotalUtilityAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.MarginPercentage).HasPrecision(18, 4).IsRequired();

        builder.Property(x => x.RequiredApproval).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder
            .HasMany(x => x.RateDetails)
            .WithOne()
            .HasForeignKey(x => x.RateHeaderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.RateDetails).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.SourceImportFclRateId);
        builder.HasIndex(x => x.AgentId);
        builder.HasIndex(x => x.CarrierId);
        builder.HasIndex(x => x.PolId);
        builder.HasIndex(x => x.PoeId);
        builder.HasIndex(x => x.PodId);
        builder.HasIndex(x => x.ContainerTypeId);
        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequiredApproval);
        builder.HasIndex(x => x.ValidFrom);
        builder.HasIndex(x => x.ValidTo);

        builder
            .HasIndex(x => x.RateCode)
            .IsUnique()
            .HasDatabaseName("ux_rate_headers_rate_code");

        builder
            .HasIndex(x => new
            {
                x.AgentId,
                x.CarrierId,
                x.PolId,
                x.PoeId,
                x.PodId,
                x.ContainerTypeId,
                x.CurrencyId,
                x.Status,
                x.ValidFrom,
                x.ValidTo,
            })
            .HasDatabaseName("ix_rate_headers_valid_lookup");

        builder
            .HasIndex(x => x.SourceImportFclRateId)
            .HasDatabaseName("ix_rate_headers_source_import_fcl_rate_id");
    }
}

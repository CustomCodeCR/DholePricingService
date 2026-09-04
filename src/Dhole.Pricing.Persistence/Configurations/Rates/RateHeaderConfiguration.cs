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

        builder.Property(x => x.ClientName).HasMaxLength(250).IsRequired(false);

        builder.Property(x => x.ExecutiveName).HasMaxLength(250).IsRequired(false);

        builder.Property(x => x.IdtraNumber).HasMaxLength(100).IsRequired(false);

        builder.Property(x => x.QuoNumber).HasMaxLength(100).IsRequired(false);

        builder.Property(x => x.Includes).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.SubjectTo).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.Excludes).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.TransitTime).HasMaxLength(160).IsRequired(false);
        builder.Property(x => x.RateType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(Dhole.Pricing.Domain.Rates.Enums.RateType.Tariff)
            .HasSentinel((Dhole.Pricing.Domain.Rates.Enums.RateType)0);

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

        builder.Property(x => x.PodId).IsRequired(false);

        builder.Property(x => x.PodName).HasMaxLength(250).IsRequired(false);

        builder.Property(x => x.PodCode).HasMaxLength(80).IsRequired(false);

        builder.Property(x => x.ContainerTypeId).IsRequired();

        builder.Property(x => x.ContainerTypeName).HasMaxLength(120).IsRequired();

        builder.Property(x => x.ContainerTypeCode).HasMaxLength(80).IsRequired();

        builder.Property(x => x.IncotermId).IsRequired(false);
        builder.Property(x => x.IncotermName).HasMaxLength(120).IsRequired(false);
        builder.Property(x => x.IncotermCode).HasMaxLength(40).IsRequired(false);

        builder.Property(x => x.PickupAddress).HasMaxLength(1000).IsRequired(false);
        builder.Property(x => x.PickupLatitude).HasPrecision(10, 7).IsRequired(false);
        builder.Property(x => x.PickupLongitude).HasPrecision(10, 7).IsRequired(false);

        builder.Property(x => x.CurrencyId).IsRequired();

        builder.Property(x => x.CurrencyName).HasMaxLength(120).IsRequired();

        builder.Property(x => x.CurrencyCode).HasMaxLength(20).IsRequired();

        builder.Property(x => x.ExchangeRatePurchase).HasPrecision(18, 6).IsRequired(false);
        builder.Property(x => x.ExchangeRateSale).HasPrecision(18, 6).IsRequired(false);
        builder.Property(x => x.ExchangeRateApplied).HasPrecision(18, 6).IsRequired(false);
        builder.Property(x => x.ExchangeRateDate).IsRequired(false);
        builder.Property(x => x.ExchangeRateCapturedAtUtc).IsRequired(false);
        builder.Property(x => x.ExchangeRateSource).HasMaxLength(160).IsRequired(false);
        builder.Property(x => x.ExchangeRateManualOverride).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.FreeDays).IsRequired();

        builder.Property(x => x.ValidFrom).IsRequired();

        builder.Property(x => x.ValidTo).IsRequired();

        builder.Property(x => x.RateCode).HasMaxLength(16).IsRequired();

        builder.Property(x => x.RateName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.RevisionNumber).IsRequired().HasDefaultValue(1);

        builder.Property(x => x.ContainerQuantity).IsRequired().HasDefaultValue(1);

        builder.Property(x => x.ShipmentMode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(Dhole.Pricing.Domain.Rates.Enums.ShipmentMode.Fcl)
            .HasSentinel((Dhole.Pricing.Domain.Rates.Enums.ShipmentMode)0);
        builder.Property(x => x.OperationType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(Dhole.Pricing.Domain.Rates.Enums.RateOperationType.TransitDomestic)
            .HasSentinel((Dhole.Pricing.Domain.Rates.Enums.RateOperationType)0);
        builder.Property(x => x.TotalPackages).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.TotalPallets).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.TotalWeightKg).HasPrecision(18, 4).IsRequired().HasDefaultValue(0m);
        builder.Property(x => x.TotalVolumeCbm).HasPrecision(18, 6).IsRequired().HasDefaultValue(0m);
        builder.Property(x => x.KgPerCbm).HasPrecision(18, 4).IsRequired().HasDefaultValue(500m);
        builder.Property(x => x.ChargeableQuantity).HasPrecision(18, 6).IsRequired().HasDefaultValue(1m);
        builder.Property(x => x.CargoLinesJson).HasColumnType("jsonb").IsRequired(false);

        builder.Property(x => x.TotalCostAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.TotalSaleAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.TotalUtilityAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalCostUsd).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalSaleUsd).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalUtilityUsd).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalCostCrc).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalSaleCrc).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalUtilityCrc).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.MarginPercentage).HasPrecision(18, 4).IsRequired();

        builder.Property(x => x.RequiredApproval).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.ClosedReason).HasMaxLength(1000).IsRequired(false);

        builder.Property(x => x.ClosedAtUtc).IsRequired(false);

        builder.Property(x => x.ClosedBy).IsRequired(false);

        builder
            .HasMany(x => x.RateServices)
            .WithOne()
            .HasForeignKey(x => x.RateHeaderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.RateServices).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(x => x.RateDetails)
            .WithOne()
            .HasForeignKey(x => x.RateHeaderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.RateDetails).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(x => x.RateContainers)
            .WithOne()
            .HasForeignKey(x => x.RateHeaderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.RateContainers).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.SourceImportFclRateId);
        builder.HasIndex(x => x.AgentId);
        builder.HasIndex(x => x.CarrierId);
        builder.HasIndex(x => x.PolId);
        builder.HasIndex(x => x.PoeId);
        builder.HasIndex(x => x.PodId);
        builder.HasIndex(x => x.ContainerTypeId);
        builder.HasIndex(x => x.ShipmentMode);
        builder.HasIndex(x => x.IncotermId);
        builder.HasIndex(x => x.RateType);
        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequiredApproval);
        builder.HasIndex(x => x.ValidFrom);
        builder.HasIndex(x => x.ValidTo);
        builder.HasIndex(x => x.IdtraNumber);
        builder.HasIndex(x => x.QuoNumber);

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

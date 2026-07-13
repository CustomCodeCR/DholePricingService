using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.Imports.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Imports;

internal sealed class ImportFclRatesConfiguration
    : EntityTypeConfigurationBase<ImportFclRates, Guid>
{
    public override void Configure(EntityTypeBuilder<ImportFclRates> builder)
    {
        base.Configure(builder);

        builder.ToTable("ImportFclRates");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ImportBatchId).IsRequired();
        builder.Property(x => x.ExtractionRecordId).IsRequired();
        builder.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        ConfigureSnapshot(builder, "ImportProfile", 100, 200);
        ConfigureSnapshot(builder, "Pol", 100, 250);
        ConfigureSnapshot(builder, "Poe", 100, 250);
        ConfigureSnapshot(builder, "Pod", 100, 250);
        ConfigureSnapshot(builder, "Carrier", 100, 250);
        ConfigureSnapshot(builder, "Agent", 100, 250);
        ConfigureSnapshot(builder, "ContainerType", 50, 150);
        ConfigureSnapshot(builder, "Currency", 20, 150);

        builder.Property(x => x.Pol).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Poe).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Pod).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Carrier).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Agent).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ContainerType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Commodity).HasMaxLength(250);

        builder.Property(x => x.Freight).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.OceanFreight).HasPrecision(18, 4);
        builder.Property(x => x.OriginCharges).HasPrecision(18, 4);
        builder.Property(x => x.DestinationCharges).HasPrecision(18, 4);
        builder.Property(x => x.Surcharges).HasPrecision(18, 4);
        builder.Property(x => x.TotalCost).HasPrecision(18, 4);
        builder.Property(x => x.TotalSale).HasPrecision(18, 4);
        builder.Property(x => x.Profit).HasPrecision(18, 4);
        builder.Property(x => x.Margin).HasPrecision(18, 4);

        builder.Property(x => x.RawDataJson).HasColumnType("jsonb");
        builder.Property(x => x.SourceUrl).HasMaxLength(1000);

        builder.HasIndex(x => x.ImportBatchId);
        builder.HasIndex(x => x.ExtractionRecordId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new
        {
            x.CarrierId,
            x.PolId,
            x.PoeId,
            x.PodId,
            x.ContainerTypeId,
        });
    }

    private static void ConfigureSnapshot(
        EntityTypeBuilder<ImportFclRates> builder,
        string prefix,
        int codeMaxLength,
        int nameMaxLength
    )
    {
        builder.Property<Guid>($"{prefix}Id").IsRequired();
        builder.Property<string>($"{prefix}Name").HasMaxLength(nameMaxLength).IsRequired();
        builder.Property<string>($"{prefix}Code").HasMaxLength(codeMaxLength).IsRequired();
        builder.Property<string>($"{prefix}Slug").HasMaxLength(200).IsRequired();
    }
}

using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.Costs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Costs;

internal sealed class CostConfiguration : EntityTypeConfigurationBase<Cost, Guid>
{
    public override void Configure(EntityTypeBuilder<Cost> builder)
    {
        base.Configure(builder);

        builder.ToTable("Costs");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(250).IsRequired();

        builder.Property(x => x.CostType).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder
            .Property(x => x.CostDetailType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CarrierId).IsRequired(false);

        builder.Property(x => x.CarrierName).HasMaxLength(250).IsRequired(false);

        builder.Property(x => x.CarrierCode).HasMaxLength(80).IsRequired(false);

        builder.Property(x => x.AgentId).IsRequired(false);

        builder.Property(x => x.AgentName).HasMaxLength(250).IsRequired(false);

        builder.Property(x => x.AgentCode).HasMaxLength(80).IsRequired(false);

        builder.Property(x => x.PortId).IsRequired();

        builder.Property(x => x.PortName).HasMaxLength(250).IsRequired();

        builder.Property(x => x.PortCode).HasMaxLength(80).IsRequired();

        builder.Property(x => x.PortRole).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.CurrencyId).IsRequired();

        builder.Property(x => x.CurrencyName).HasMaxLength(120).IsRequired();

        builder.Property(x => x.CurrencyCode).HasMaxLength(20).IsRequired();

        builder.Property(x => x.CostAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.SaleAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.UtilityAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.Notes).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.CostType);
        builder.HasIndex(x => x.CostDetailType);
        builder.HasIndex(x => x.CarrierId);
        builder.HasIndex(x => x.AgentId);
        builder.HasIndex(x => x.PortId);
        builder.HasIndex(x => x.PortRole);
        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.IsActive);

        builder
            .HasIndex(x => new
            {
                x.CostType,
                x.CostDetailType,
                x.CarrierId,
                x.AgentId,
                x.PortId,
                x.PortRole,
                x.Name,
                x.CurrencyId,
            })
            .HasDatabaseName("ix_costs_template_unique")
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}

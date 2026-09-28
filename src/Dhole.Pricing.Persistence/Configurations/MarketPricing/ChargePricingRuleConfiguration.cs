using Dhole.Pricing.Domain.MarketPricing.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.MarketPricing;

internal sealed class ChargePricingRuleConfiguration
    : IEntityTypeConfiguration<ChargePricingRule>
{
    public void Configure(EntityTypeBuilder<ChargePricingRule> builder)
    {
        builder.ToTable("ChargePricingRules");

        builder.HasKey(x => x.Id).HasName("PK_ChargePricingRules");
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");

        builder.Property(x => x.ChargeCode).HasMaxLength(120).HasColumnName("charge_code").IsRequired();
        builder.Property(x => x.CanAutoAdjust).HasColumnName("can_auto_adjust");
        builder.Property(x => x.AdjustmentPriority).HasColumnName("adjustment_priority");
        builder.Property(x => x.MinimumMarkup).HasPrecision(9, 4).HasColumnName("minimum_markup");
        builder.Property(x => x.MaximumMarkup).HasPrecision(9, 4).HasColumnName("maximum_markup");
        builder.Property(x => x.MaximumAdjustmentAmount).HasPrecision(18, 2).HasColumnName("maximum_adjustment_amount");
        builder.Property(x => x.AdjustmentStrategy)
            .HasConversion<string>()
            .HasMaxLength(40)
            .HasColumnName("adjustment_strategy")
            .IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active");

        builder.HasIndex(x => x.ChargeCode)
            .IsUnique()
            .HasDatabaseName("UX_ChargePricingRules_ChargeCode");

        builder.HasIndex(x => new { x.IsActive, x.AdjustmentPriority })
            .HasDatabaseName("IX_ChargePricingRules_Active_Priority");
    }
}

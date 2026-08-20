using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateContainerAllocationConfiguration
    : EntityTypeConfigurationBase<RateContainerAllocation, Guid>
{
    public override void Configure(EntityTypeBuilder<RateContainerAllocation> builder)
    {
        base.Configure(builder);

        builder.ToTable("RateContainerAllocations");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RateHeaderId).IsRequired();
        builder.Property(x => x.ContainerTypeId).IsRequired();
        builder.Property(x => x.ContainerTypeName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ContainerTypeCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();

        builder
            .HasIndex(x => x.RateHeaderId)
            .HasDatabaseName("i_x_rate_container_allocations_rate_header_id");
        builder
            .HasIndex(x => x.ContainerTypeId)
            .HasDatabaseName("i_x_rate_container_allocations_container_type_id");
        builder
            .HasIndex(x => new { x.RateHeaderId, x.ContainerTypeId })
            .IsUnique()
            .HasDatabaseName("ux_rate_container_allocations_rate_container");
    }
}

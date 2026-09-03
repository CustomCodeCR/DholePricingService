using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateRequestConfiguration : IEntityTypeConfiguration<RateRequest>
{
    public void Configure(EntityTypeBuilder<RateRequest> builder)
    {
        // RateRequests is created by the explicit SQL migration
        // 20260903203000_AddSellerRateRequests. Excluding this table from the
        // convention-based migration differ prevents EF Core from treating this
        // externally-described table as a pending model change before MigrateAsync
        // has a chance to execute that migration.
        builder.ToTable("RateRequests", "pricing", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired();
        builder.Property(x => x.DueAtUtc).HasColumnName("due_at_utc").IsRequired();
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.SlaReminderSentAtUtc).HasColumnName("sla_reminder_sent_at_utc");
        builder.Property(x => x.RateId).HasColumnName("rate_id");
        builder.Property(x => x.SellerUserId).HasColumnName("seller_user_id");
        builder.Property(x => x.SellerName).HasColumnName("seller_name").HasMaxLength(200);
        builder.Property(x => x.SellerEmail).HasColumnName("seller_email").HasMaxLength(320);
        builder.Property(x => x.ClientName).HasColumnName("client_name").HasMaxLength(250);
        builder.Property(x => x.ExecutiveName).HasColumnName("executive_name").HasMaxLength(200);
        builder.Property(x => x.ShipmentMode).HasColumnName("shipment_mode").HasMaxLength(32);
        builder.Property(x => x.OriginName).HasColumnName("origin_name").HasMaxLength(250);
        builder.Property(x => x.DestinationName).HasColumnName("destination_name").HasMaxLength(250);
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => new { x.Status, x.Priority, x.DueAtUtc, x.RequestedAtUtc });
        builder.HasIndex(x => x.RateId);
    }
}

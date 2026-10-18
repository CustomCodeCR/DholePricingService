using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261017184500_AddSellerRateReceipt")]
public sealed class AddSellerRateReceipt : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.AddColumn<DateTime>(name: "seller_received_at_utc", schema: "pricing",
            table: "RateHeaders", type: "timestamp with time zone", nullable: true);
        builder.AddColumn<Guid>(name: "seller_received_by", schema: "pricing",
            table: "RateHeaders", type: "uuid", nullable: true);
    }

    protected override void Down(MigrationBuilder builder)
    {
        builder.DropColumn(name: "seller_received_at_utc", schema: "pricing", table: "RateHeaders");
        builder.DropColumn(name: "seller_received_by", schema: "pricing", table: "RateHeaders");
    }
}

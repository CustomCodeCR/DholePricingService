using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261103160000_AddRateClientAcceptedAtUtc")]
public sealed class AddRateClientAcceptedAtUtc : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "accepted_at_utc",
            schema: "pricing",
            table: "RateHeaders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RateHeaders_accepted_at_utc",
            schema: "pricing",
            table: "RateHeaders",
            column: "accepted_at_utc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RateHeaders_accepted_at_utc",
            schema: "pricing",
            table: "RateHeaders");
        migrationBuilder.DropColumn(
            name: "accepted_at_utc",
            schema: "pricing",
            table: "RateHeaders");
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261001090000_AddRatePickupLocationsJson")]
public sealed class AddRatePickupLocationsJson : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "pickup_locations_json",
            schema: "pricing",
            table: "RateHeaders",
            type: "jsonb",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "pickup_locations_json",
            schema: "pricing",
            table: "RateHeaders"
        );
    }
}

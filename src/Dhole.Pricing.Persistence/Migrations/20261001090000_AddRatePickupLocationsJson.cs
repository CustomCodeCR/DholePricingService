using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

public partial class AddRatePickupLocationsJson : Migration
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

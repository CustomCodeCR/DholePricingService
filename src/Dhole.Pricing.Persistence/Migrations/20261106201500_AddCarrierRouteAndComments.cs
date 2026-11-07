using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261106201500_AddCarrierRouteAndComments")]
public sealed class AddCarrierRouteAndComments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "carrier_route", schema: "pricing", table: "RateHeaders",
            type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "carrier_comments", schema: "pricing", table: "RateHeaders",
            type: "text", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "carrier_comments", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "carrier_route", schema: "pricing", table: "RateHeaders");
    }
}

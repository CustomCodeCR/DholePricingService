using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;
[DbContext(typeof(ServiceDbContext))]
[Migration("20261019183000_AddRateDeclaredCargoValue")]
public sealed class AddRateDeclaredCargoValue : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.AddColumn<decimal>(name: "cargo_value", schema: "pricing", table: "RateHeaders",
            type: "numeric(18,2)", nullable: false, defaultValue: 0m);
    }
    protected override void Down(MigrationBuilder builder)
    {
        builder.DropColumn(name: "cargo_value", schema: "pricing", table: "RateHeaders");
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261015235000_AddPerUnitContainerWeights")]
public sealed class AddPerUnitContainerWeights : Migration
{
    protected override void Up(MigrationBuilder builder) =>
        builder.AddColumn<string>(name: "unit_weights_kg_json", schema: "pricing",
            table: "RateContainerAllocations", type: "text", nullable: false, defaultValue: "[]");
    protected override void Down(MigrationBuilder builder) =>
        builder.DropColumn(name: "unit_weights_kg_json", schema: "pricing", table: "RateContainerAllocations");
}

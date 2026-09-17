using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

public partial class AddRateWarehouseSelection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "warehouse_id",
            schema: "pricing",
            table: "RateHeaders",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_rate_headers_warehouse_id",
            schema: "pricing",
            table: "RateHeaders",
            column: "warehouse_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_rate_headers_warehouse_id",
            schema: "pricing",
            table: "RateHeaders");

        migrationBuilder.DropColumn(
            name: "warehouse_id",
            schema: "pricing",
            table: "RateHeaders");
    }
}

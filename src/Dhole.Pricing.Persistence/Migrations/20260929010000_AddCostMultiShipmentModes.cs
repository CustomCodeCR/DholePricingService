using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260929010000_AddCostMultiShipmentModes")]
public partial class AddCostMultiShipmentModes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "shipment_mode_mask",
            schema: "pricing",
            table: "Costs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.Sql("""
            UPDATE pricing."Costs"
            SET shipment_mode_mask = CASE shipment_mode
                WHEN 'Fcl' THEN 1
                WHEN 'Lcl' THEN 2
                WHEN 'Ftl' THEN 4
                WHEN 'Ltl' THEN 8
                WHEN 'Air' THEN 16
                WHEN 'AirConsol' THEN 32
                ELSE 0
            END;
            """);

        migrationBuilder.DropIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs");

        migrationBuilder.CreateIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs",
            columns: new[]
            {
                "cost_type",
                "cost_detail_type",
                "carrier_id",
                "agent_id",
                "port_id",
                "port_role",
                "pol_id",
                "poe_id",
                "pod_id",
                "shipment_mode_mask",
                "charge_basis",
                "is_accountant",
                "name",
                "currency_id",
            },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(
            name: "IX_Costs_shipment_mode_mask",
            schema: "pricing",
            table: "Costs",
            column: "shipment_mode_mask");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Costs_shipment_mode_mask",
            schema: "pricing",
            table: "Costs");

        migrationBuilder.DropIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs");

        migrationBuilder.CreateIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs",
            columns: new[]
            {
                "cost_type",
                "cost_detail_type",
                "carrier_id",
                "agent_id",
                "port_id",
                "port_role",
                "pol_id",
                "poe_id",
                "pod_id",
                "shipment_mode",
                "charge_basis",
                "is_accountant",
                "name",
                "currency_id",
            },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.DropColumn(
            name: "shipment_mode_mask",
            schema: "pricing",
            table: "Costs");
    }
}

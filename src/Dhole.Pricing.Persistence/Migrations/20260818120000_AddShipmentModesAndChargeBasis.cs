using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260818120000_AddShipmentModesAndChargeBasis")]
public partial class AddShipmentModesAndChargeBasis : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "shipment_mode",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Fcl");
        migrationBuilder.AddColumn<int>(name: "total_packages", schema: "pricing", table: "RateHeaders", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "total_pallets", schema: "pricing", table: "RateHeaders", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<decimal>(name: "total_weight_kg", schema: "pricing", table: "RateHeaders", type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "total_volume_cbm", schema: "pricing", table: "RateHeaders", type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "kg_per_cbm", schema: "pricing", table: "RateHeaders", type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 500m);
        migrationBuilder.AddColumn<decimal>(name: "chargeable_quantity", schema: "pricing", table: "RateHeaders", type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 1m);
        migrationBuilder.AddColumn<string>(name: "cargo_lines_json", schema: "pricing", table: "RateHeaders", type: "jsonb", nullable: true);

        migrationBuilder.AddColumn<string>(name: "charge_basis", schema: "pricing", table: "RateDetails", type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "PerShipment");
        migrationBuilder.Sql("ALTER TABLE pricing.\"RateDetails\" ALTER COLUMN quantity TYPE numeric(18,6) USING quantity::numeric(18,6);");
        migrationBuilder.Sql("UPDATE pricing.\"RateDetails\" SET charge_basis = 'PerContainer' WHERE cost_detail_type IN ('Freight','InlandTransport');");

        migrationBuilder.AddColumn<string>(name: "shipment_mode", schema: "pricing", table: "Costs", type: "character varying(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>(name: "charge_basis", schema: "pricing", table: "Costs", type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "PerShipment");
        migrationBuilder.AddColumn<decimal>(name: "minimum_cost_amount", schema: "pricing", table: "Costs", type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "minimum_sale_amount", schema: "pricing", table: "Costs", type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "kg_per_cbm", schema: "pricing", table: "Costs", type: "numeric(18,4)", precision: 18, scale: 4, nullable: true);
        // Preserve the legacy meaning of is_accountant. Before charge_basis existed,
        // true meant "por contenedor" for every cost, not only Freight/InlandTransport.
        migrationBuilder.Sql("UPDATE pricing.\"Costs\" SET shipment_mode = COALESCE(shipment_mode, 'Fcl'), charge_basis = 'PerContainer' WHERE is_accountant = true;");
        // A legacy one-time documentation charge represents one BL/document.
        migrationBuilder.Sql("UPDATE pricing.\"Costs\" SET charge_basis = 'PerDocument' WHERE is_accountant = false AND cost_detail_type = 'Documentation';");

        migrationBuilder.DropIndex(name: "ix_costs_template_unique", schema: "pricing", table: "Costs");
        migrationBuilder.CreateIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs",
            columns: new[] { "cost_type", "cost_detail_type", "carrier_id", "agent_id", "port_id", "port_role", "pol_id", "poe_id", "pod_id", "shipment_mode", "charge_basis", "is_accountant", "name", "currency_id" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.CreateIndex(name: "IX_RateHeaders_shipment_mode", schema: "pricing", table: "RateHeaders", column: "shipment_mode");
        migrationBuilder.CreateIndex(name: "IX_RateDetails_charge_basis", schema: "pricing", table: "RateDetails", column: "charge_basis");
        migrationBuilder.CreateIndex(name: "IX_Costs_shipment_mode", schema: "pricing", table: "Costs", column: "shipment_mode");
        migrationBuilder.CreateIndex(name: "IX_Costs_charge_basis", schema: "pricing", table: "Costs", column: "charge_basis");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_RateHeaders_shipment_mode", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropIndex(name: "IX_RateDetails_charge_basis", schema: "pricing", table: "RateDetails");
        migrationBuilder.DropIndex(name: "IX_Costs_shipment_mode", schema: "pricing", table: "Costs");
        migrationBuilder.DropIndex(name: "IX_Costs_charge_basis", schema: "pricing", table: "Costs");

        migrationBuilder.DropIndex(name: "ix_costs_template_unique", schema: "pricing", table: "Costs");
        migrationBuilder.CreateIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs",
            columns: new[] { "cost_type", "cost_detail_type", "carrier_id", "agent_id", "port_id", "port_role", "pol_id", "poe_id", "pod_id", "is_accountant", "name", "currency_id" },
            unique: true,
            filter: "is_deleted = false");

        migrationBuilder.Sql("ALTER TABLE pricing.\"RateDetails\" ALTER COLUMN quantity TYPE integer USING ROUND(quantity)::integer;");
        migrationBuilder.DropColumn(name: "charge_basis", schema: "pricing", table: "RateDetails");

        foreach (var column in new[] { "shipment_mode", "total_packages", "total_pallets", "total_weight_kg", "total_volume_cbm", "kg_per_cbm", "chargeable_quantity", "cargo_lines_json" })
            migrationBuilder.DropColumn(name: column, schema: "pricing", table: "RateHeaders");

        foreach (var column in new[] { "shipment_mode", "charge_basis", "minimum_cost_amount", "minimum_sale_amount", "kg_per_cbm" })
            migrationBuilder.DropColumn(name: column, schema: "pricing", table: "Costs");
    }
}

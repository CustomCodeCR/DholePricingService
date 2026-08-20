using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

public partial class AddCostRouteConditions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs");

        migrationBuilder.AddColumn<Guid>(name: "pol_id", schema: "pricing", table: "Costs", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "pol_name", schema: "pricing", table: "Costs", type: "character varying(250)", maxLength: 250, nullable: true);
        migrationBuilder.AddColumn<string>(name: "pol_code", schema: "pricing", table: "Costs", type: "character varying(80)", maxLength: 80, nullable: true);

        migrationBuilder.AddColumn<Guid>(name: "poe_id", schema: "pricing", table: "Costs", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "poe_name", schema: "pricing", table: "Costs", type: "character varying(250)", maxLength: 250, nullable: true);
        migrationBuilder.AddColumn<string>(name: "poe_code", schema: "pricing", table: "Costs", type: "character varying(80)", maxLength: 80, nullable: true);

        migrationBuilder.AddColumn<Guid>(name: "pod_id", schema: "pricing", table: "Costs", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "pod_name", schema: "pricing", table: "Costs", type: "character varying(250)", maxLength: 250, nullable: true);
        migrationBuilder.AddColumn<string>(name: "pod_code", schema: "pricing", table: "Costs", type: "character varying(80)", maxLength: 80, nullable: true);

        migrationBuilder.CreateIndex(name: "IX_Costs_pol_id", schema: "pricing", table: "Costs", column: "pol_id");
        migrationBuilder.CreateIndex(name: "IX_Costs_poe_id", schema: "pricing", table: "Costs", column: "poe_id");
        migrationBuilder.CreateIndex(name: "IX_Costs_pod_id", schema: "pricing", table: "Costs", column: "pod_id");

        migrationBuilder.CreateIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs",
            columns: new[]
            {
                "cost_type", "cost_detail_type", "carrier_id", "agent_id", "port_id", "port_role",
                "pol_id", "poe_id", "pod_id", "is_accountant", "name", "currency_id"
            },
            unique: true,
            filter: "is_deleted = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_costs_template_unique", schema: "pricing", table: "Costs");
        migrationBuilder.DropIndex(name: "IX_Costs_pol_id", schema: "pricing", table: "Costs");
        migrationBuilder.DropIndex(name: "IX_Costs_poe_id", schema: "pricing", table: "Costs");
        migrationBuilder.DropIndex(name: "IX_Costs_pod_id", schema: "pricing", table: "Costs");

        migrationBuilder.DropColumn(name: "pol_id", schema: "pricing", table: "Costs");
        migrationBuilder.DropColumn(name: "pol_name", schema: "pricing", table: "Costs");
        migrationBuilder.DropColumn(name: "pol_code", schema: "pricing", table: "Costs");
        migrationBuilder.DropColumn(name: "poe_id", schema: "pricing", table: "Costs");
        migrationBuilder.DropColumn(name: "poe_name", schema: "pricing", table: "Costs");
        migrationBuilder.DropColumn(name: "poe_code", schema: "pricing", table: "Costs");
        migrationBuilder.DropColumn(name: "pod_id", schema: "pricing", table: "Costs");
        migrationBuilder.DropColumn(name: "pod_name", schema: "pricing", table: "Costs");
        migrationBuilder.DropColumn(name: "pod_code", schema: "pricing", table: "Costs");

        migrationBuilder.CreateIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs",
            columns: new[]
            {
                "cost_type", "cost_detail_type", "carrier_id", "agent_id", "port_id", "port_role",
                "is_accountant", "name", "currency_id"
            },
            unique: true,
            filter: "is_deleted = false");
    }
}

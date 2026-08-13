using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncIncotermModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_cost_incoterms__costs_cost_id",
                schema: "pricing",
                table: "CostIncoterms");

            migrationBuilder.DropPrimaryKey(
                name: "p_k_cost_incoterms",
                schema: "pricing",
                table: "CostIncoterms");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CostIncoterms",
                schema: "pricing",
                table: "CostIncoterms",
                columns: new[] { "cost_id", "incoterm_id" });

            migrationBuilder.AddForeignKey(
                name: "f_k_cost_incoterms_costs_cost_id",
                schema: "pricing",
                table: "CostIncoterms",
                column: "cost_id",
                principalSchema: "pricing",
                principalTable: "Costs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_cost_incoterms_costs_cost_id",
                schema: "pricing",
                table: "CostIncoterms");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CostIncoterms",
                schema: "pricing",
                table: "CostIncoterms");

            migrationBuilder.AddPrimaryKey(
                name: "p_k_cost_incoterms",
                schema: "pricing",
                table: "CostIncoterms",
                columns: new[] { "cost_id", "incoterm_id" });

            migrationBuilder.AddForeignKey(
                name: "f_k_cost_incoterms__costs_cost_id",
                schema: "pricing",
                table: "CostIncoterms",
                column: "cost_id",
                principalSchema: "pricing",
                principalTable: "Costs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

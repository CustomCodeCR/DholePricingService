using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRateComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RateComparisons",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_import_fcl_rate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compared_rate_header_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compared_rate_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    comparison_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    pol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pol_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    poe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poe_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    container_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_type_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    baseline_cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    baseline_sale_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    candidate_cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    candidate_sale_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    baseline_compared_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    candidate_compared_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    savings_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    savings_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    candidate_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_rate_header_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_rate_comparisons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "RateComparisonDetails",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_comparison_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    cost_detail_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cost_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    charge_basis = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    baseline_cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    baseline_sale_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    candidate_cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    candidate_sale_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_rate_comparison_details", x => x.id);
                    table.ForeignKey(
                        name: "f_k_rate_comparison_details_rate_comparisons_rate_comparison_id",
                        column: x => x.rate_comparison_id,
                        principalSchema: "pricing",
                        principalTable: "RateComparisons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_rate_comparison_details_rate_comparison_id",
                schema: "pricing",
                table: "RateComparisonDetails",
                column: "rate_comparison_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateComparisons_pol_id_poe_id_container_type_id",
                schema: "pricing",
                table: "RateComparisons",
                columns: new[] { "pol_id", "poe_id", "container_type_id" });

            migrationBuilder.CreateIndex(
                name: "IX_RateComparisons_source_import_fcl_rate_id_compared_rate_hea~",
                schema: "pricing",
                table: "RateComparisons",
                columns: new[] { "source_import_fcl_rate_id", "compared_rate_header_id", "comparison_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RateComparisons_status_created_at_utc",
                schema: "pricing",
                table: "RateComparisons",
                columns: new[] { "status", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RateComparisonDetails",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "RateComparisons",
                schema: "pricing");
        }
    }
}

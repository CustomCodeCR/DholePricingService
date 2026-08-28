using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingServiceCurrencies20260828 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "operation_type",
                schema: "pricing",
                table: "RateHeaders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "TransitDomestic");

            migrationBuilder.AddColumn<decimal>(
                name: "total_cost_crc",
                schema: "pricing",
                table: "RateHeaders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_cost_usd",
                schema: "pricing",
                table: "RateHeaders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_sale_crc",
                schema: "pricing",
                table: "RateHeaders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_sale_usd",
                schema: "pricing",
                table: "RateHeaders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_utility_crc",
                schema: "pricing",
                table: "RateHeaders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_utility_usd",
                schema: "pricing",
                table: "RateHeaders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CostServices",
                schema: "pricing",
                columns: table => new
                {
                    cost_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    service_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostServices", x => new { x.cost_id, x.service_id });
                    table.ForeignKey(
                        name: "f_k_cost_service_costs_cost_id",
                        column: x => x.cost_id,
                        principalSchema: "pricing",
                        principalTable: "Costs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RateServices",
                schema: "pricing",
                columns: table => new
                {
                    rate_header_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    service_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateServices", x => new { x.rate_header_id, x.service_id });
                    table.ForeignKey(
                        name: "f_k_rate_service_rate_headers_rate_header_id",
                        column: x => x.rate_header_id,
                        principalSchema: "pricing",
                        principalTable: "RateHeaders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostServices_service_code",
                schema: "pricing",
                table: "CostServices",
                column: "service_code");

            migrationBuilder.CreateIndex(
                name: "IX_CostServices_service_id",
                schema: "pricing",
                table: "CostServices",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateServices_service_code",
                schema: "pricing",
                table: "RateServices",
                column: "service_code");

            migrationBuilder.CreateIndex(
                name: "IX_RateServices_service_id",
                schema: "pricing",
                table: "RateServices",
                column: "service_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostServices",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "RateServices",
                schema: "pricing");

            migrationBuilder.DropColumn(
                name: "operation_type",
                schema: "pricing",
                table: "RateHeaders");

            migrationBuilder.DropColumn(
                name: "total_cost_crc",
                schema: "pricing",
                table: "RateHeaders");

            migrationBuilder.DropColumn(
                name: "total_cost_usd",
                schema: "pricing",
                table: "RateHeaders");

            migrationBuilder.DropColumn(
                name: "total_sale_crc",
                schema: "pricing",
                table: "RateHeaders");

            migrationBuilder.DropColumn(
                name: "total_sale_usd",
                schema: "pricing",
                table: "RateHeaders");

            migrationBuilder.DropColumn(
                name: "total_utility_crc",
                schema: "pricing",
                table: "RateHeaders");

            migrationBuilder.DropColumn(
                name: "total_utility_usd",
                schema: "pricing",
                table: "RateHeaders");
        }
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260813203000_AddIncotermsToRatesAndCosts")]
public sealed class AddIncotermsToRatesAndCosts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "incoterm_id",
            schema: "pricing",
            table: "RateHeaders",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "incoterm_name",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "incoterm_code",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CostIncoterms",
            schema: "pricing",
            columns: table => new
            {
                cost_id = table.Column<Guid>(type: "uuid", nullable: false),
                incoterm_id = table.Column<Guid>(type: "uuid", nullable: false),
                incoterm_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                incoterm_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("p_k_cost_incoterms", x => new { x.cost_id, x.incoterm_id });
                table.ForeignKey(
                    name: "f_k_cost_incoterms__costs_cost_id",
                    column: x => x.cost_id,
                    principalSchema: "pricing",
                    principalTable: "Costs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CostIncoterms_incoterm_code",
            schema: "pricing",
            table: "CostIncoterms",
            column: "incoterm_code");

        migrationBuilder.CreateIndex(
            name: "IX_CostIncoterms_incoterm_id",
            schema: "pricing",
            table: "CostIncoterms",
            column: "incoterm_id");

        migrationBuilder.CreateIndex(
            name: "IX_RateHeaders_incoterm_id",
            schema: "pricing",
            table: "RateHeaders",
            column: "incoterm_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CostIncoterms",
            schema: "pricing");

        migrationBuilder.DropIndex(
            name: "IX_RateHeaders_incoterm_id",
            schema: "pricing",
            table: "RateHeaders");

        migrationBuilder.DropColumn(name: "incoterm_id", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "incoterm_name", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "incoterm_code", schema: "pricing", table: "RateHeaders");
    }
}

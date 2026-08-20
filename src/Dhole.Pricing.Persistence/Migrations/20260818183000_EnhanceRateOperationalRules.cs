using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260818183000_EnhanceRateOperationalRules")]
public partial class EnhanceRateOperationalRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "rate_type",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Tariff");

        migrationBuilder.AddColumn<string>(
            name: "transit_time",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(160)",
            maxLength: 160,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE pricing."RateHeaders"
            SET transit_time = CASE
                WHEN transit_days IS NULL THEN NULL
                ELSE transit_days::text || ' días'
            END;
            """);

        migrationBuilder.DropColumn(name: "transit_days", schema: "pricing", table: "RateHeaders");
        migrationBuilder.CreateIndex(name: "IX_RateHeaders_rate_type", schema: "pricing", table: "RateHeaders", column: "rate_type");

        migrationBuilder.CreateTable(
            name: "CarrierFreeDayRules",
            schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                carrier_id = table.Column<Guid>(type: "uuid", nullable: false),
                carrier_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                carrier_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                free_days = table.Column<int>(type: "integer", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_CarrierFreeDayRules", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_CarrierFreeDayRules_carrier_id",
            schema: "pricing",
            table: "CarrierFreeDayRules",
            column: "carrier_id",
            unique: true);

        migrationBuilder.CreateTable(
            name: "RateTermBlocks",
            schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                rate_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                shipment_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                poe_id = table.Column<Guid>(type: "uuid", nullable: true),
                poe_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                poe_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                incoterm_id = table.Column<Guid>(type: "uuid", nullable: true),
                incoterm_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                incoterm_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_RateTermBlocks", x => x.id));

        migrationBuilder.CreateTable(
            name: "RateTermBlockItems",
            schema: "pricing",
            columns: table => new
            {
                block_id = table.Column<Guid>(type: "uuid", nullable: false),
                rate_term_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RateTermBlockItems", x => new { x.block_id, x.rate_term_item_id });
                table.ForeignKey(
                    name: "FK_RateTermBlockItems_RateTermBlocks_block_id",
                    column: x => x.block_id,
                    principalSchema: "pricing",
                    principalTable: "RateTermBlocks",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_RateTermBlockItems_RateTermItems_rate_term_item_id",
                    column: x => x.rate_term_item_id,
                    principalSchema: "pricing",
                    principalTable: "RateTermItems",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_RateTermBlockItems_rate_term_item_id", schema: "pricing", table: "RateTermBlockItems", column: "rate_term_item_id");
        migrationBuilder.CreateIndex(name: "IX_RateTermBlocks_lookup", schema: "pricing", table: "RateTermBlocks", columns: new[] { "rate_type", "shipment_mode", "poe_id", "incoterm_id", "is_active" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "RateTermBlockItems", schema: "pricing");
        migrationBuilder.DropTable(name: "RateTermBlocks", schema: "pricing");
        migrationBuilder.DropTable(name: "CarrierFreeDayRules", schema: "pricing");
        migrationBuilder.DropIndex(name: "IX_RateHeaders_rate_type", schema: "pricing", table: "RateHeaders");

        migrationBuilder.AddColumn<int>(name: "transit_days", schema: "pricing", table: "RateHeaders", type: "integer", nullable: true);
        migrationBuilder.Sql("""
            UPDATE pricing."RateHeaders"
            SET transit_days = CASE
                WHEN transit_time ~ '^[[:space:]]*[0-9]+' THEN substring(transit_time from '[0-9]+')::integer
                ELSE NULL
            END;
            """);
        migrationBuilder.DropColumn(name: "transit_time", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "rate_type", schema: "pricing", table: "RateHeaders");
    }
}

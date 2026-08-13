using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260811100000_AddRateTermItems")]
public sealed class AddRateTermItems : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RateTermItems", schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("p_k_rate_term_items", x => x.id)
        );
        migrationBuilder.CreateIndex(
            name: "IX_RateTermItems_type_is_active_sort_order", schema: "pricing", table: "RateTermItems",
            columns: new[] { "type", "is_active", "sort_order" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "RateTermItems", schema: "pricing");
}

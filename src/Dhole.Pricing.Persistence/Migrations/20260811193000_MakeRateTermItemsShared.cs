using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260811193000_MakeRateTermItemsShared")]
public sealed class MakeRateTermItemsShared : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RateTermItems_type_is_active_sort_order",
            schema: "pricing",
            table: "RateTermItems");

        // Un ítem es compartido por las tres categorías. Si existían copias del mismo
        // texto en categorías diferentes, conservamos una sola antes de quitar type.
        migrationBuilder.Sql("""
            DELETE FROM pricing."RateTermItems" a
            USING pricing."RateTermItems" b
            WHERE lower(a.text) = lower(b.text)
              AND a.id > b.id;
            """);

        migrationBuilder.DropColumn(
            name: "type",
            schema: "pricing",
            table: "RateTermItems");

        migrationBuilder.CreateIndex(
            name: "IX_RateTermItems_is_active_sort_order",
            schema: "pricing",
            table: "RateTermItems",
            columns: new[] { "is_active", "sort_order" });

        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RateTermItems_text_ci"
            ON pricing."RateTermItems" (lower(text));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS pricing.\"IX_RateTermItems_text_ci\";");

        migrationBuilder.DropIndex(
            name: "IX_RateTermItems_is_active_sort_order",
            schema: "pricing",
            table: "RateTermItems");

        migrationBuilder.AddColumn<string>(
            name: "type",
            schema: "pricing",
            table: "RateTermItems",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "Includes");

        migrationBuilder.CreateIndex(
            name: "IX_RateTermItems_type_is_active_sort_order",
            schema: "pricing",
            table: "RateTermItems",
            columns: new[] { "type", "is_active", "sort_order" });
    }
}

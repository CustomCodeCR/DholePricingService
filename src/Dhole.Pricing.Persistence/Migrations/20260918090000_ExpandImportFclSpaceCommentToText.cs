using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260918090000_ExpandImportFclSpaceCommentToText")]
public sealed class ExpandImportFclSpaceCommentToText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "space_comment",
            schema: "pricing",
            table: "ImportFclRates",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(2000)",
            oldMaxLength: 2000,
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."ImportFclRates"
            SET space_comment = LEFT(space_comment, 2000)
            WHERE space_comment IS NOT NULL
              AND length(space_comment) > 2000;
            """
        );

        migrationBuilder.AlterColumn<string>(
            name: "space_comment",
            schema: "pricing",
            table: "ImportFclRates",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);
    }
}

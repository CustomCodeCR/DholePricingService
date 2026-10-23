using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261023134000_AddMultipleIdtraAndOtherReferences")]
public sealed class AddMultipleIdtraAndOtherReferences : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.AlterColumn<string>(
            name: "idtra_number", schema: "pricing", table: "RateHeaders",
            type: "character varying(2000)", maxLength: 2000, nullable: true,
            oldClrType: typeof(string), oldType: "character varying(100)",
            oldMaxLength: 100, oldNullable: true);
        builder.AddColumn<string>(
            name: "other_references", schema: "pricing", table: "RateHeaders",
            type: "character varying(2000)", maxLength: 2000, nullable: true);
    }

    protected override void Down(MigrationBuilder builder)
    {
        // Protect existing multi-IDTRA values: rollback cannot silently truncate.
        builder.Sql("""
            DO $$
            BEGIN
              IF EXISTS (SELECT 1 FROM pricing."RateHeaders"
                         WHERE length(idtra_number) > 100)
              THEN RAISE EXCEPTION 'Cannot down-migrate: IDTRA data exceeds 100 characters';
              END IF;
            END $$;
            """);
        builder.DropColumn(name: "other_references", schema: "pricing", table: "RateHeaders");
        builder.AlterColumn<string>(
            name: "idtra_number", schema: "pricing", table: "RateHeaders",
            type: "character varying(100)", maxLength: 100, nullable: true,
            oldClrType: typeof(string), oldType: "character varying(2000)",
            oldMaxLength: 2000, oldNullable: true);
    }
}

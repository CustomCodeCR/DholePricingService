using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

/// <summary>
/// RateHeader.IdtraNumber supports multiple IDTRA references (up to 2000 chars),
/// but RateRevision still had the original varchar(100). Capturing the previous
/// revision on each PUT therefore failed with PostgreSQL SQLSTATE 22001.
/// Widen the archive column without rewriting existing commercial snapshots.
/// </summary>
[DbContext(typeof(ServiceDbContext))]
[Migration("20261118231500_ExpandRateRevisionIdtraNumber")]
public sealed class ExpandRateRevisionIdtraNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "IdtraNumber",
            schema: "pricing",
            table: "RateRevisions",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100,
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A rollback must never silently corrupt or truncate historical IDTRAs.
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM pricing."RateRevisions"
                    WHERE length("IdtraNumber") > 100
                ) THEN
                    RAISE EXCEPTION 'Cannot shrink RateRevisions.IdtraNumber: archived values exceed 100 characters';
                END IF;
            END $$;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "IdtraNumber",
            schema: "pricing",
            table: "RateRevisions",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(2000)",
            oldMaxLength: 2000,
            oldNullable: true);
    }
}

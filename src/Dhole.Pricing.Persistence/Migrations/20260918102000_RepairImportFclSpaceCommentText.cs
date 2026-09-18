using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260918102000_RepairImportFclSpaceCommentText")]
public sealed class RepairImportFclSpaceCommentText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Production may contain an EF migration-history entry from a deployment
        // where the physical column remained varchar(2000). Force the actual schema
        // to the model's expected type without truncating any existing data.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'pricing'
                      AND table_name = 'ImportFclRates'
                      AND column_name = 'space_comment'
                      AND (
                          data_type <> 'text'
                          OR character_maximum_length IS NOT NULL
                      )
                ) THEN
                    ALTER TABLE pricing."ImportFclRates"
                    ALTER COLUMN space_comment TYPE text;
                END IF;
            END
            $$;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally non-destructive. A rollback must not truncate imported
        // commercial conditions that may exceed the old 2000-character limit.
    }
}

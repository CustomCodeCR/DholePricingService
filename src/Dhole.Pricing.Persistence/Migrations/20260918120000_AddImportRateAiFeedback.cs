using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260918120000_AddImportRateAiFeedback")]
public sealed class AddImportRateAiFeedback : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."ImportRateAiFeedback"
            (
                import_rate_id uuid PRIMARY KEY,
                confirmed_against_source boolean NOT NULL DEFAULT FALSE,
                outcome varchar(32) NOT NULL,
                reason_codes_json jsonb NOT NULL DEFAULT '[]'::jsonb,
                comment varchar(2000),
                correct_value varchar(2000),
                original_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
                reviewed_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
                reviewed_by uuid,
                reviewed_at_utc timestamptz NOT NULL,
                updated_at_utc timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS "IX_ImportRateAiFeedback_ReviewedAtUtc"
                ON pricing."ImportRateAiFeedback" (reviewed_at_utc DESC);

            CREATE INDEX IF NOT EXISTS "IX_ImportRateAiFeedback_Outcome"
                ON pricing."ImportRateAiFeedback" (outcome);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS pricing."ImportRateAiFeedback";
            """
        );
    }
}

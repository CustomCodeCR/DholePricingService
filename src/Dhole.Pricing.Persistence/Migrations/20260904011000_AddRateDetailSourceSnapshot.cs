using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260904011000_AddRateDetailSourceSnapshot")]
public sealed class AddRateDetailSourceSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateDetails"
                ADD COLUMN IF NOT EXISTS source_type character varying(40) NOT NULL DEFAULT 'Manual';

            ALTER TABLE pricing."RateDetails"
                ADD COLUMN IF NOT EXISTS source_reference character varying(300) NULL;

            UPDATE pricing."RateDetails"
            SET source_type = CASE
                WHEN "CostId" IS NOT NULL THEN 'CostCatalog'
                WHEN "CostType" = 'Fixed' THEN 'ExternalSnapshot'
                ELSE 'Manual'
            END;

            CREATE INDEX IF NOT EXISTS ix_rate_details_source
                ON pricing."RateDetails" ("RateHeaderId", source_type);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS pricing.ix_rate_details_source;
            ALTER TABLE pricing."RateDetails" DROP COLUMN IF EXISTS source_reference;
            ALTER TABLE pricing."RateDetails" DROP COLUMN IF EXISTS source_type;
            """
        );
    }
}

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
        // RateDetails already stores name, basis, currency, cost, sale, quantity and notes
        // even when cost_id is null. source_type makes that case explicit without forcing
        // external LCL/coloader charges into the Costs catalog. The generated value also
        // keeps future rows correctly classified without duplicating this rule in Web.
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateDetails"
                ADD COLUMN IF NOT EXISTS source_type character varying(40)
                GENERATED ALWAYS AS (
                    CASE
                        WHEN cost_id IS NOT NULL THEN 'CostCatalog'
                        WHEN cost_type = 'Fixed' THEN 'ExternalSnapshot'
                        ELSE 'Manual'
                    END
                ) STORED;

            CREATE INDEX IF NOT EXISTS ix_rate_details_source
                ON pricing."RateDetails" (rate_header_id, source_type);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS pricing.ix_rate_details_source;
            ALTER TABLE pricing."RateDetails" DROP COLUMN IF EXISTS source_type;
            """
        );
    }
}

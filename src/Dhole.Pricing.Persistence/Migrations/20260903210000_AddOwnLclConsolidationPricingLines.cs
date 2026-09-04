using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260903210000_AddOwnLclConsolidationPricingLines")]
public sealed class AddOwnLclConsolidationPricingLines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."OwnLclConsolidationPricingLines" (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                consolidation_id uuid NOT NULL,
                line_key varchar(80) NOT NULL,
                cost_unit numeric(18,6) NOT NULL DEFAULT 0,
                sale_unit numeric(18,6) NOT NULL DEFAULT 0,
                updated_at_utc timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT fk_own_lcl_pricing_lines_consolidation
                    FOREIGN KEY (consolidation_id)
                    REFERENCES pricing."OwnLclConsolidations"(id)
                    ON DELETE CASCADE,
                CONSTRAINT uq_own_lcl_pricing_lines UNIQUE (consolidation_id, line_key)
            );

            CREATE INDEX IF NOT EXISTS ix_own_lcl_pricing_lines_consolidation
                ON pricing."OwnLclConsolidationPricingLines" (consolidation_id);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP TABLE IF EXISTS pricing."OwnLclConsolidationPricingLines";""");
    }
}

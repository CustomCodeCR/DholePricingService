using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260922153500_RealignOwnLclCentralAmericaPricingLines")]
public sealed class RealignOwnLclCentralAmericaPricingLines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- CA_HANDLING historically represented "Manejos destino".
            -- Preserve every configured cost/sale under the new explicit key before
            -- CA_HANDLING starts representing the separate "Manejos" charge.
            INSERT INTO pricing."OwnLclConsolidationPricingLines"
                (id, consolidation_id, line_key, cost_unit, sale_unit, updated_at_utc)
            SELECT
                gen_random_uuid(),
                consolidation_id,
                'CA_DESTINATION_HANDLING',
                cost_unit,
                sale_unit,
                now()
            FROM pricing."OwnLclConsolidationPricingLines"
            WHERE upper(line_key) = 'CA_HANDLING'
            ON CONFLICT (consolidation_id, line_key) DO NOTHING;

            DELETE FROM pricing."OwnLclConsolidationPricingLines"
            WHERE upper(line_key) = 'CA_HANDLING';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data-only compatibility migration. Existing configured values are kept
        // under their explicit semantic key to avoid destructive rollback.
    }
}

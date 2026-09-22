using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260922175000_BackfillOwnLclCentralAmericaRouteCosts")]
public sealed class BackfillOwnLclCentralAmericaRouteCosts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- These keys were introduced with zero cost defaults. They represent
            -- the CNCA-024/#049 route-cost components and must carry their real
            -- per-CBM costs so Pantalla 7 matches the consolidation matrix.
            UPDATE pricing."OwnLclConsolidationPricingLines"
            SET cost_unit = CASE upper(line_key)
                WHEN 'CA_TRANSSHIPMENT' THEN 39.719736842105264
                WHEN 'CA_INLAND_NI' THEN 16.428571428571429
                WHEN 'CA_INLAND_HN' THEN 26.071428571428573
                WHEN 'CA_INLAND_GT' THEN 35
                WHEN 'CA_INLAND_SV' THEN 31.428571428571429
                WHEN 'CA_STUFFING' THEN 5.928571428571429
                ELSE cost_unit
            END,
            updated_at_utc = now()
            WHERE cost_unit = 0
              AND upper(line_key) IN (
                'CA_TRANSSHIPMENT',
                'CA_INLAND_NI',
                'CA_INLAND_HN',
                'CA_INLAND_GT',
                'CA_INLAND_SV',
                'CA_STUFFING'
              );
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data correction only; do not erase configured consolidation costs.
    }
}

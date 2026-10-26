using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261025191000_RealignMaerskBalboaOwnLclPricingLines")]
public sealed class RealignMaerskBalboaOwnLclPricingLines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Reprice only derived cost/CBM values left over from the old USD 2175
        // Maersk Balboa profile. Preserve every manually edited cost and ALL sales.
        migrationBuilder.Sql(
            """
            UPDATE pricing."OwnLclConsolidationPricingLines" AS l
            SET cost_unit =
                    ROUND(912.00 / GREATEST(c.maximum_cbm, 0.01), 6)
                    + CASE WHEN l.line_key = 'CA_TRANSSHIPMENT' THEN 9.00 ELSE 0.00 END,
                updated_at_utc = now()
            FROM pricing."OwnLclConsolidations" AS c
            WHERE c.id = l.consolidation_id
              AND c.is_active = TRUE
              AND c.destination_profile_version = 'MATRIX-LIVE'
              AND c.carrier_destination_cost_total = 912.00
              AND (c.destination_charge_snapshot_json ->> 'totalCost')::numeric = 912.00
              AND (
                  UPPER(BTRIM(COALESCE(c.carrier_code, ''))) LIKE 'MAERSK%'
                  OR UPPER(BTRIM(COALESCE(c.carrier_name, ''))) LIKE 'MAERSK%'
              )
              AND UPPER(BTRIM(COALESCE(c.panama_arrival_port_code, ''))) = 'POE-2026-003'
              AND l.line_key IN ('PA_DESTINATION_CHARGE', 'CA_TRANSSHIPMENT')
              AND l.cost_unit =
                  ROUND(2175.00 / GREATEST(c.maximum_cbm, 0.01), 6)
                  + CASE WHEN l.line_key = 'CA_TRANSSHIPMENT' THEN 9.00 ELSE 0.00 END;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not roll back commercial costs to the incorrect profile.
    }
}

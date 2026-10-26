using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261025185000_CorrectMaerskBalboaOwnLclDestinationCharges")]
public sealed class CorrectMaerskBalboaOwnLclDestinationCharges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Only correct existing MAERSK/Balboa snapshots that still hold the
        // original USD 2175 reference. Respect later manually edited totals.
        // The amounts and keys here are verified against the original matrix
        // profile; all other carriers, ports and consolidations stay unchanged.
        migrationBuilder.Sql(
            """
            WITH affected AS (
                SELECT c.id, c.destination_charge_snapshot_json AS snapshot
                FROM pricing."OwnLclConsolidations" c
                WHERE c.is_active = TRUE
                  AND c.destination_profile_version = 'MATRIX-LIVE'
                  AND c.carrier_destination_cost_total = 2175.00
                  AND (
                      UPPER(BTRIM(COALESCE(c.carrier_code, ''))) LIKE 'MAERSK%'
                      OR UPPER(BTRIM(COALESCE(c.carrier_name, ''))) LIKE 'MAERSK%'
                  )
                  AND UPPER(BTRIM(COALESCE(c.panama_arrival_port_code, ''))) = 'POE-2026-003'
            ),
            wanted AS (
                SELECT a.id, elem.charge
                FROM affected a
                CROSS JOIN LATERAL jsonb_array_elements(a.snapshot -> 'charges') AS elem(charge)
                WHERE elem.charge ->> 'code' IN (
                    'COST-396edf7694d34d92a533aa6b8501044b',
                    'COST-19cec538ca344ddd8e5660b45a1272e4',
                    'COST-523029b29303499cbb76c7d48c697709',
                    'COST-fd5dd670ba9044b49b491ce78d9480b8'
                )
            ),
            repriced AS (
                SELECT id,
                    COUNT(*) AS included_count,
                    SUM((charge ->> 'amount')::numeric) AS total,
                    jsonb_agg(
                        charge ORDER BY CASE charge ->> 'code'
                            WHEN 'COST-396edf7694d34d92a533aa6b8501044b' THEN 1
                            WHEN 'COST-19cec538ca344ddd8e5660b45a1272e4' THEN 2
                            WHEN 'COST-523029b29303499cbb76c7d48c697709' THEN 3
                            WHEN 'COST-fd5dd670ba9044b49b491ce78d9480b8' THEN 4
                            ELSE 5 END
                    ) AS selected_charges
                FROM wanted
                GROUP BY id
            )
            UPDATE pricing."OwnLclConsolidations" AS c
            SET carrier_destination_cost_total = r.total,
                destination_charge_snapshot_json =
                    jsonb_set(
                        jsonb_set(
                            jsonb_set(
                                c.destination_charge_snapshot_json,
                                '{charges}', r.selected_charges, false),
                            '{totalCost}', to_jsonb(r.total), true),
                        '{costPerCbm}',
                        to_jsonb(r.total / GREATEST(c.maximum_cbm, 0.01)), true),
                updated_at_utc = now()
            FROM repriced r
            WHERE c.id = r.id
              AND r.included_count = 4
              AND r.total = 912.00;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not restore incorrect charges or overwrite later price changes.
    }
}

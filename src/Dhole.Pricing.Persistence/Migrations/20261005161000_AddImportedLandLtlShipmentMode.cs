using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261005161000_AddImportedLandLtlShipmentMode")]
public sealed class AddImportedLandLtlShipmentMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."ImportFclRates"
                DROP CONSTRAINT IF EXISTS "CK_ImportFclRates_ShipmentMode";

            WITH normalized AS (
                SELECT
                    id,
                    regexp_replace(lower(coalesce(raw_data_json::text, '')), '[^a-z0-9]+', '', 'g') AS raw_text
                FROM pricing."ImportFclRates"
            )
            UPDATE pricing."ImportFclRates" rates
            SET shipment_mode = 'Ltl',
                container_type_id = '4f0f5cf1-c43b-4c6e-a70d-7eb657817442'::uuid,
                container_type = 'LTL',
                container_type_name = 'LTL',
                container_type_code = 'LTL',
                container_type_slug = 'ltl'
            FROM normalized n
            WHERE rates.id = n.id
              AND rates.shipment_mode = 'Unknown'
              AND (
                    n.raw_text LIKE '%tariffmodeltl%'
                 OR n.raw_text LIKE '%shipmentmodeltl%'
                 OR n.raw_text LIKE '%servicemodeltl%'
                 OR n.raw_text LIKE '%loadtypeltl%'
                 OR n.raw_text LIKE '%lessthantruckload%'
                 OR n.raw_text LIKE '%ltlterrestre%'
                 OR n.raw_text LIKE '%terrestreltl%'
                 OR n.raw_text LIKE '%landltl%'
              );

            ALTER TABLE pricing."ImportFclRates"
                ADD CONSTRAINT "CK_ImportFclRates_ShipmentMode"
                CHECK (shipment_mode IN ('Unknown', 'Fcl', 'LclColoader', 'AirLclColoader', 'Ltl'));
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."ImportFclRates"
                DROP CONSTRAINT IF EXISTS "CK_ImportFclRates_ShipmentMode";

            UPDATE pricing."ImportFclRates"
            SET shipment_mode = 'Unknown'
            WHERE shipment_mode = 'Ltl';

            ALTER TABLE pricing."ImportFclRates"
                ADD CONSTRAINT "CK_ImportFclRates_ShipmentMode"
                CHECK (shipment_mode IN ('Unknown', 'Fcl', 'LclColoader', 'AirLclColoader'));
            """
        );
    }
}

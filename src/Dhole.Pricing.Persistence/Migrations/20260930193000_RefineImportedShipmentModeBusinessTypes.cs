using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260930193000_RefineImportedShipmentModeBusinessTypes")]
public sealed class RefineImportedShipmentModeBusinessTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."ImportFclRates"
                DROP CONSTRAINT IF EXISTS "CK_ImportFclRates_ShipmentMode";

            UPDATE pricing."ImportFclRates"
            SET shipment_mode = CASE
                WHEN shipment_mode = 'Lcl' THEN 'LclColoader'
                WHEN shipment_mode = 'Air' THEN 'AirLclColoader'
                ELSE shipment_mode
            END;

            WITH normalized AS (
                SELECT
                    id,
                    regexp_replace(
                        lower(coalesce(raw_data_json::text, '')),
                        '[^a-z0-9]+',
                        '',
                        'g'
                    ) AS raw_text
                FROM pricing."ImportFclRates"
            )
            UPDATE pricing."ImportFclRates" rates
            SET shipment_mode = 'AirLclColoader',
                container_type_id = '321ae516-76a1-10ed-6d98-2117496f8ff4'::uuid,
                container_type = 'AIR',
                container_type_name = 'AIR',
                container_type_code = 'AIR',
                container_type_slug = 'air'
            FROM normalized n
            WHERE rates.id = n.id
              AND (
                    lower(coalesce(rates.container_type, '')) = 'air'
                 OR lower(coalesce(rates.container_type_name, '')) = 'air'
                 OR lower(coalesce(rates.container_type_code, '')) = 'air'
                 OR lower(coalesce(rates.container_type_slug, '')) = 'air'
                 OR n.raw_text LIKE '%tariffmodeair%'
                 OR n.raw_text LIKE '%shipmentmodeair%'
                 OR n.raw_text LIKE '%servicemodeair%'
                 OR n.raw_text LIKE '%containertypeair%'
                 OR n.raw_text LIKE '%equipoair%'
                 OR n.raw_text LIKE '%airconsolidated%'
                 OR n.raw_text LIKE '%airbacktoback%'
                 OR n.raw_text LIKE '%airlineroute%'
                 OR n.raw_text LIKE '%ratebasiskgvol%'
                 OR (
                        n.raw_text LIKE '%kgpercbm167%'
                        OR n.raw_text LIKE '%1cbm167kg%'
                        OR n.raw_text LIKE '%167kgcbm%'
                    )
                 OR (
                        (
                            n.raw_text LIKE '%airline%'
                            OR n.raw_text LIKE '%aerolinea%'
                            OR n.raw_text LIKE '%aerolnea%'
                        )
                        AND (
                            n.raw_text LIKE '%167kg%'
                            OR n.raw_text LIKE '%kgvol%'
                            OR n.raw_text LIKE '%volumetric%'
                        )
                    )
              );

            WITH normalized AS (
                SELECT
                    id,
                    regexp_replace(
                        lower(coalesce(raw_data_json::text, '')),
                        '[^a-z0-9]+',
                        '',
                        'g'
                    ) AS raw_text
                FROM pricing."ImportFclRates"
            )
            UPDATE pricing."ImportFclRates" rates
            SET shipment_mode = 'LclColoader',
                container_type_id = 'f4d19764-7556-2a0d-9222-42d7b48d00d8'::uuid,
                container_type = 'LCL',
                container_type_name = 'LCL',
                container_type_code = 'LCL',
                container_type_slug = 'lcl'
            FROM normalized n
            WHERE rates.id = n.id
              AND rates.shipment_mode <> 'AirLclColoader'
              AND (
                    lower(coalesce(rates.container_type, '')) = 'lcl'
                 OR lower(coalesce(rates.container_type_name, '')) = 'lcl'
                 OR lower(coalesce(rates.container_type_code, '')) = 'lcl'
                 OR lower(coalesce(rates.container_type_slug, '')) = 'lcl'
                 OR n.raw_text LIKE '%equipolcl%'
                 OR n.raw_text LIKE '%containertypelcl%'
                 OR n.raw_text LIKE '%tariffmodelcl%'
                 OR n.raw_text LIKE '%shipmentmodelcl%'
                 OR n.raw_text LIKE '%lessthancontainerload%'
                 OR n.raw_text LIKE '%unitwm%'
                 OR n.raw_text LIKE '%groupage%'
                 OR n.raw_text LIKE '%coloader%'
                 OR n.raw_text LIKE '%coloading%'
              );

            WITH normalized AS (
                SELECT
                    id,
                    regexp_replace(
                        lower(coalesce(raw_data_json::text, '')),
                        '[^a-z0-9]+',
                        '',
                        'g'
                    ) AS raw_text
                FROM pricing."ImportFclRates"
            )
            UPDATE pricing."ImportFclRates" rates
            SET shipment_mode = 'Fcl'
            FROM normalized n
            WHERE rates.id = n.id
              AND rates.shipment_mode = 'Unknown'
              AND (
                    lower(coalesce(rates.container_type_code, '')) ~ '^(20|40|45)(dv|dc|gp|hc|hq|std|rf|rh|ot|fr|nor)'
                 OR lower(coalesce(rates.container_type_name, '')) ~ '(20|40|45).*(dry|high|reefer|open|flat|standard)'
                 OR n.raw_text LIKE '%tariffmodefcl%'
                 OR n.raw_text LIKE '%shipmentmodefcl%'
              );

            ALTER TABLE pricing."ImportFclRates"
                ADD CONSTRAINT "CK_ImportFclRates_ShipmentMode"
                CHECK (shipment_mode IN ('Unknown', 'Fcl', 'LclColoader', 'AirLclColoader'));

            CREATE INDEX IF NOT EXISTS "IX_ImportFclRates_ShipmentMode"
                ON pricing."ImportFclRates" (shipment_mode);
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
            SET shipment_mode = CASE
                WHEN shipment_mode = 'LclColoader' THEN 'Lcl'
                WHEN shipment_mode = 'AirLclColoader' THEN 'Air'
                ELSE shipment_mode
            END;

            ALTER TABLE pricing."ImportFclRates"
                ADD CONSTRAINT "CK_ImportFclRates_ShipmentMode"
                CHECK (shipment_mode IN ('Unknown', 'Fcl', 'Lcl', 'Air'));
            """
        );
    }
}

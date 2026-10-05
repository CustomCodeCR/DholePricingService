using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261005205500_NormalizeImportedRateTransportDimensions")]
public sealed class NormalizeImportedRateTransportDimensions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."ImportFclRates"
                DROP CONSTRAINT IF EXISTS "CK_ImportFclRates_ShipmentMode";

            -- Keep a compact audit of rows whose canonical logistics snapshots
            -- are changed by this normalization. RawDataJson is intentionally
            -- preserved as immutable extraction evidence.
            CREATE TABLE IF NOT EXISTS pricing."ImportRateNormalizationAudit"
            (
                normalization_version character varying(40) NOT NULL,
                import_rate_id uuid NOT NULL,
                normalized_at_utc timestamp with time zone NOT NULL DEFAULT NOW(),
                previous_shipment_mode character varying(30) NOT NULL,
                previous_carrier_id uuid NOT NULL,
                previous_carrier_name character varying(250) NOT NULL,
                previous_carrier_code character varying(100) NOT NULL,
                previous_carrier_slug character varying(200) NOT NULL,
                previous_container_type_id uuid NOT NULL,
                previous_container_type_name character varying(150) NOT NULL,
                previous_container_type_code character varying(50) NOT NULL,
                previous_container_type_slug character varying(200) NOT NULL,
                CONSTRAINT "PK_ImportRateNormalizationAudit"
                    PRIMARY KEY (normalization_version, import_rate_id)
            );

            INSERT INTO pricing."ImportRateNormalizationAudit"
            (
                normalization_version,
                import_rate_id,
                previous_shipment_mode,
                previous_carrier_id,
                previous_carrier_name,
                previous_carrier_code,
                previous_carrier_slug,
                previous_container_type_id,
                previous_container_type_name,
                previous_container_type_code,
                previous_container_type_slug
            )
            SELECT
                '20261005-transport-v1',
                id,
                shipment_mode,
                carrier_id,
                carrier_name,
                carrier_code,
                carrier_slug,
                container_type_id,
                container_type_name,
                container_type_code,
                container_type_slug
            FROM pricing."ImportFclRates"
            WHERE shipment_mode IN ('Unknown', 'Lcl', 'Air')
               OR (
                    shipment_mode = 'LclColoader'
                    AND lower(coalesce(container_type_code, '')) <> 'lcl'
               )
               OR (
                    shipment_mode = 'AirLclColoader'
                    AND lower(coalesce(container_type_code, '')) <> 'air'
               )
               OR (
                    shipment_mode = 'Ltl'
                    AND lower(coalesce(container_type_code, '')) <> 'ltl'
               )
               OR lower(coalesce(carrier_slug, '')) IN
                    ('lcl-consolidado', 'aereo', 'terrestre-ltl')
            ON CONFLICT (normalization_version, import_rate_id) DO NOTHING;

            -- Legacy aliases, if any, are collapsed to the canonical business modes.
            UPDATE pricing."ImportFclRates"
            SET shipment_mode = CASE
                WHEN shipment_mode = 'Lcl' THEN 'LclColoader'
                WHEN shipment_mode = 'Air' THEN 'AirLclColoader'
                ELSE shipment_mode
            END
            WHERE shipment_mode IN ('Lcl', 'Air');

            -- Unknown historical rows are classified only from explicit, high-confidence
            -- modality/equipment evidence. Existing FCL rows are intentionally preserved.
            WITH evidence AS
            (
                SELECT
                    id,
                    lower(coalesce(container_type_code, '')) AS equipment_code,
                    lower(coalesce(container_type_name, '')) AS equipment_name,
                    regexp_replace(lower(coalesce(raw_data_json::text, '')), '[^a-z0-9]+', '', 'g') AS raw_text
                FROM pricing."ImportFclRates"
                WHERE shipment_mode = 'Unknown'
            )
            UPDATE pricing."ImportFclRates" rates
            SET shipment_mode = CASE
                WHEN e.equipment_code = 'air'
                  OR e.equipment_name = 'air'
                  OR e.raw_text LIKE '%tariffmodeair%'
                  OR e.raw_text LIKE '%shipmentmodeair%'
                  OR e.raw_text LIKE '%servicemodeair%'
                  OR e.raw_text LIKE '%ratebasiskgvol%'
                  OR e.raw_text LIKE '%airconsolidated%'
                  OR e.raw_text LIKE '%airbacktoback%'
                    THEN 'AirLclColoader'
                WHEN e.equipment_code = 'ltl'
                  OR e.equipment_name = 'ltl'
                  OR e.raw_text LIKE '%tariffmodeltl%'
                  OR e.raw_text LIKE '%shipmentmodeltl%'
                  OR e.raw_text LIKE '%servicemodeltl%'
                  OR e.raw_text LIKE '%lessthantruckload%'
                  OR e.raw_text LIKE '%ltlterrestre%'
                  OR e.raw_text LIKE '%terrestreltl%'
                    THEN 'Ltl'
                WHEN e.equipment_code = 'lcl'
                  OR e.equipment_name = 'lcl'
                  OR e.raw_text LIKE '%tariffmodelcl%'
                  OR e.raw_text LIKE '%shipmentmodelcl%'
                  OR e.raw_text LIKE '%lessthancontainerload%'
                  OR e.raw_text LIKE '%groupage%'
                  OR e.raw_text LIKE '%unitwm%'
                  OR e.raw_text LIKE '%coloading%'
                  OR e.raw_text LIKE '%coloader%'
                    THEN 'LclColoader'
                WHEN e.equipment_code ~ '^(20|40|45)(dv|dc|gp|hc|hq|std|rf|rh|ot|fr|nor)'
                  OR e.equipment_name ~ '(20|40|45).*(dry|high|reefer|open|flat|standard)'
                  OR e.raw_text LIKE '%tariffmodefcl%'
                  OR e.raw_text LIKE '%shipmentmodefcl%'
                    THEN 'Fcl'
                ELSE rates.shipment_mode
            END
            FROM evidence e
            WHERE rates.id = e.id;

            -- Canonical equipment snapshot by business mode.
            UPDATE pricing."ImportFclRates"
            SET container_type_id = 'f4d19764-7556-2a0d-9222-42d7b48d00d8'::uuid,
                container_type = 'LCL',
                container_type_name = 'LCL',
                container_type_code = 'LCL',
                container_type_slug = 'lcl',
                updated_at_utc = CASE
                    WHEN lower(coalesce(container_type_code, '')) <> 'lcl' THEN NOW()
                    ELSE updated_at_utc
                END
            WHERE shipment_mode = 'LclColoader';

            UPDATE pricing."ImportFclRates"
            SET container_type_id = '321ae516-76a1-10ed-6d98-2117496f8ff4'::uuid,
                container_type = 'AIR',
                container_type_name = 'AIR',
                container_type_code = 'AIR',
                container_type_slug = 'air',
                updated_at_utc = CASE
                    WHEN lower(coalesce(container_type_code, '')) <> 'air' THEN NOW()
                    ELSE updated_at_utc
                END
            WHERE shipment_mode = 'AirLclColoader';

            UPDATE pricing."ImportFclRates"
            SET container_type_id = '4f0f5cf1-c43b-4c6e-a70d-7eb657817442'::uuid,
                container_type = 'LTL',
                container_type_name = 'LTL',
                container_type_code = 'LTL',
                container_type_slug = 'ltl',
                updated_at_utc = CASE
                    WHEN lower(coalesce(container_type_code, '')) <> 'ltl' THEN NOW()
                    ELSE updated_at_utc
                END
            WHERE shipment_mode = 'Ltl';

            -- Generic modality labels are not real carriers. For consolidated
            -- imports the coloader belongs in Agent; Carrier remains pending unless
            -- the source names an actual shipping line / airline / transporter.
            UPDATE pricing."ImportFclRates"
            SET carrier_id = '9eb7d950-ef7e-7dd7-b304-a60fa63d646d'::uuid,
                carrier = 'PORASIGNAR',
                carrier_name = 'Por asignar',
                carrier_code = 'PORASIGNAR',
                carrier_slug = 'por-asignar',
                updated_at_utc = NOW()
            WHERE shipment_mode IN ('LclColoader', 'AirLclColoader', 'Ltl')
              AND lower(coalesce(carrier_slug, '')) IN
                    ('lcl-consolidado', 'aereo', 'terrestre-ltl');

            ALTER TABLE pricing."ImportFclRates"
                ADD CONSTRAINT "CK_ImportFclRates_ShipmentMode"
                CHECK (shipment_mode IN ('Unknown', 'Fcl', 'LclColoader', 'AirLclColoader', 'Ltl'));

            -- Separate the physical transport dimension from consolidation/load mode.
            -- These generated columns can never drift away from shipment_mode.
            ALTER TABLE pricing."ImportFclRates"
                ADD COLUMN IF NOT EXISTS transport_mode character varying(16)
                    GENERATED ALWAYS AS
                    (
                        CASE shipment_mode
                            WHEN 'Fcl' THEN 'Maritime'
                            WHEN 'LclColoader' THEN 'Maritime'
                            WHEN 'AirLclColoader' THEN 'Air'
                            WHEN 'Ltl' THEN 'Land'
                            ELSE 'Unknown'
                        END
                    ) STORED,
                ADD COLUMN IF NOT EXISTS load_mode character varying(16)
                    GENERATED ALWAYS AS
                    (
                        CASE shipment_mode
                            WHEN 'Fcl' THEN 'Fcl'
                            WHEN 'LclColoader' THEN 'Lcl'
                            WHEN 'AirLclColoader' THEN 'Lcl'
                            WHEN 'Ltl' THEN 'Ltl'
                            ELSE 'Unknown'
                        END
                    ) STORED,
                ADD COLUMN IF NOT EXISTS is_coloader boolean
                    GENERATED ALWAYS AS
                    (
                        shipment_mode IN ('LclColoader', 'AirLclColoader', 'Ltl')
                    ) STORED;

            COMMENT ON COLUMN pricing."ImportFclRates".transport_mode
                IS 'Canonical physical mode: Maritime, Land, Air or Unknown.';
            COMMENT ON COLUMN pricing."ImportFclRates".load_mode
                IS 'Canonical load/consolidation mode: Fcl, Lcl, Ltl or Unknown.';
            COMMENT ON COLUMN pricing."ImportFclRates".is_coloader
                IS 'True for consolidated coloader rates; false for FCL/unknown.';

            CREATE INDEX IF NOT EXISTS "IX_ImportFclRates_TransportMode"
                ON pricing."ImportFclRates" (transport_mode);

            CREATE INDEX IF NOT EXISTS "IX_ImportFclRates_TransportLoadStatus"
                ON pricing."ImportFclRates" (transport_mode, load_mode, is_coloader, status);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS pricing."IX_ImportFclRates_TransportLoadStatus";
            DROP INDEX IF EXISTS pricing."IX_ImportFclRates_TransportMode";

            ALTER TABLE pricing."ImportFclRates"
                DROP COLUMN IF EXISTS is_coloader,
                DROP COLUMN IF EXISTS load_mode,
                DROP COLUMN IF EXISTS transport_mode;

            -- Canonical shipment/equipment/carrier cleanup is intentionally not reversed.
            """
        );
    }
}

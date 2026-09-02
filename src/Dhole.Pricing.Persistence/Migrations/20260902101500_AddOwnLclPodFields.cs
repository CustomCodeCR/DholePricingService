using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260902101500_AddOwnLclPodFields")]
public sealed class AddOwnLclPodFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."OwnLclConsolidations"
                ADD COLUMN IF NOT EXISTS pod_id uuid,
                ADD COLUMN IF NOT EXISTS pod_name varchar(200),
                ADD COLUMN IF NOT EXISTS pod_code varchar(80);

            UPDATE pricing."OwnLclConsolidations"
            SET pod_id = NULL,
                pod_name = COALESCE(
                    NULLIF(BTRIM(destination_charge_snapshot_json->>'finalRatePointName'), ''),
                    NULLIF(BTRIM(pod_name), ''),
                    panama_arrival_port_name
                ),
                pod_code = COALESCE(
                    NULLIF(BTRIM(destination_charge_snapshot_json->>'finalRatePointCode'), ''),
                    NULLIF(BTRIM(pod_code), ''),
                    panama_arrival_port_code
                );

            CREATE OR REPLACE FUNCTION pricing.sync_own_lcl_pod_from_destination_profile()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                NEW.pod_id := NULL;
                NEW.pod_name := COALESCE(
                    NULLIF(BTRIM(NEW.destination_charge_snapshot_json->>'finalRatePointName'), ''),
                    NEW.panama_arrival_port_name
                );
                NEW.pod_code := COALESCE(
                    NULLIF(BTRIM(NEW.destination_charge_snapshot_json->>'finalRatePointCode'), ''),
                    NEW.panama_arrival_port_code
                );
                RETURN NEW;
            END;
            $function$;

            DROP TRIGGER IF EXISTS "TRG_OwnLclConsolidations_SyncPod" ON pricing."OwnLclConsolidations";
            CREATE TRIGGER "TRG_OwnLclConsolidations_SyncPod"
            BEFORE INSERT OR UPDATE OF destination_charge_snapshot_json, panama_arrival_port_id, panama_arrival_port_name, panama_arrival_port_code
            ON pricing."OwnLclConsolidations"
            FOR EACH ROW
            EXECUTE FUNCTION pricing.sync_own_lcl_pod_from_destination_profile();

            CREATE INDEX IF NOT EXISTS "IX_OwnLclConsolidations_Pod"
                ON pricing."OwnLclConsolidations" (pod_code);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS "TRG_OwnLclConsolidations_SyncPod" ON pricing."OwnLclConsolidations";
            DROP FUNCTION IF EXISTS pricing.sync_own_lcl_pod_from_destination_profile();
            DROP INDEX IF EXISTS pricing."IX_OwnLclConsolidations_Pod";
            ALTER TABLE pricing."OwnLclConsolidations"
                DROP COLUMN IF EXISTS pod_code,
                DROP COLUMN IF EXISTS pod_name,
                DROP COLUMN IF EXISTS pod_id;
            """
        );
    }
}

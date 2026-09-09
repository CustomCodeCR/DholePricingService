using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260906161000_AddRateRequestPoePod")]
public sealed class AddRateRequestPoePod : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateRequests"
                ADD COLUMN IF NOT EXISTS poe_id uuid NULL,
                ADD COLUMN IF NOT EXISTS poe_name character varying(250) NULL,
                ADD COLUMN IF NOT EXISTS pod_id uuid NULL,
                ADD COLUMN IF NOT EXISTS pod_name character varying(250) NULL;

            UPDATE pricing."RateRequests"
            SET
                poe_id = COALESCE(
                    poe_id,
                    CASE
                        WHEN COALESCE(payload_json -> 'requestContext' ->> 'poeId', payload_json -> 'form' ->> 'destinationId', '')
                             ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$'
                        THEN COALESCE(payload_json -> 'requestContext' ->> 'poeId', payload_json -> 'form' ->> 'destinationId')::uuid
                        ELSE NULL
                    END
                ),
                poe_name = COALESCE(
                    poe_name,
                    NULLIF(payload_json -> 'requestContext' ->> 'poeName', ''),
                    destination_name
                ),
                pod_id = COALESCE(
                    pod_id,
                    CASE
                        WHEN COALESCE(payload_json -> 'requestContext' ->> 'podId', payload_json -> 'form' ->> 'podId', '')
                             ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$'
                        THEN COALESCE(payload_json -> 'requestContext' ->> 'podId', payload_json -> 'form' ->> 'podId')::uuid
                        ELSE NULL
                    END
                ),
                pod_name = COALESCE(
                    pod_name,
                    NULLIF(payload_json -> 'requestContext' ->> 'podName', '')
                );

            CREATE INDEX IF NOT EXISTS "IX_RateRequests_poe_id"
                ON pricing."RateRequests" (poe_id);

            CREATE INDEX IF NOT EXISTS "IX_RateRequests_pod_id"
                ON pricing."RateRequests" (pod_id);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS pricing."IX_RateRequests_poe_id";
            DROP INDEX IF EXISTS pricing."IX_RateRequests_pod_id";

            ALTER TABLE pricing."RateRequests"
                DROP COLUMN IF EXISTS poe_id,
                DROP COLUMN IF EXISTS poe_name,
                DROP COLUMN IF EXISTS pod_id,
                DROP COLUMN IF EXISTS pod_name;
            """
        );
    }
}

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
            SET pod_id = COALESCE(pod_id, panama_arrival_port_id),
                pod_name = COALESCE(NULLIF(BTRIM(pod_name), ''), panama_arrival_port_name),
                pod_code = COALESCE(NULLIF(BTRIM(pod_code), ''), panama_arrival_port_code)
            WHERE pod_id IS NULL
               OR NULLIF(BTRIM(pod_name), '') IS NULL
               OR NULLIF(BTRIM(pod_code), '') IS NULL;

            CREATE INDEX IF NOT EXISTS "IX_OwnLclConsolidations_Pod"
                ON pricing."OwnLclConsolidations" (pod_code);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS pricing."IX_OwnLclConsolidations_Pod";
            ALTER TABLE pricing."OwnLclConsolidations"
                DROP COLUMN IF EXISTS pod_code,
                DROP COLUMN IF EXISTS pod_name,
                DROP COLUMN IF EXISTS pod_id;
            """
        );
    }
}

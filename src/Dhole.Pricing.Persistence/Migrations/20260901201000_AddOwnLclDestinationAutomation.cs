using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260901201000_AddOwnLclDestinationAutomation")]
public sealed class AddOwnLclDestinationAutomation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."OwnLclConsolidations"
                ADD COLUMN IF NOT EXISTS panama_arrival_port_id uuid,
                ADD COLUMN IF NOT EXISTS panama_arrival_port_name varchar(200),
                ADD COLUMN IF NOT EXISTS panama_arrival_port_code varchar(80),
                ADD COLUMN IF NOT EXISTS destination_profile_code varchar(120),
                ADD COLUMN IF NOT EXISTS destination_profile_version varchar(120),
                ADD COLUMN IF NOT EXISTS destination_charge_snapshot_json jsonb,
                ADD COLUMN IF NOT EXISTS include_empty_return boolean NOT NULL DEFAULT TRUE;

            CREATE INDEX IF NOT EXISTS "IX_OwnLclConsolidations_CarrierArrivalPort"
                ON pricing."OwnLclConsolidations" (carrier_code, panama_arrival_port_code);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS pricing."IX_OwnLclConsolidations_CarrierArrivalPort";
            ALTER TABLE pricing."OwnLclConsolidations"
                DROP COLUMN IF EXISTS include_empty_return,
                DROP COLUMN IF EXISTS destination_charge_snapshot_json,
                DROP COLUMN IF EXISTS destination_profile_version,
                DROP COLUMN IF EXISTS destination_profile_code,
                DROP COLUMN IF EXISTS panama_arrival_port_code,
                DROP COLUMN IF EXISTS panama_arrival_port_name,
                DROP COLUMN IF EXISTS panama_arrival_port_id;
            """
        );
    }
}

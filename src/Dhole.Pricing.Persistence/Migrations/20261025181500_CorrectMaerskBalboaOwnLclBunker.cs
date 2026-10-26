using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261025181500_CorrectMaerskBalboaOwnLclBunker")]
public sealed class CorrectMaerskBalboaOwnLclBunker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Correct only existing MAERSK / Balboa projects that still have the
        // previous automated bunker baseline. Other carriers, ports and
        // manually configured amounts other than 280 are left unchanged.
        migrationBuilder.Sql(
            """
            UPDATE pricing."OwnLclConsolidations"
            SET bunker_cost = 476.00,
                destination_charge_snapshot_json =
                    CASE
                        WHEN destination_charge_snapshot_json #>> '{costaRicaTransfer,bunker}' = '280'
                        THEN jsonb_set(
                            destination_charge_snapshot_json,
                            '{costaRicaTransfer,bunker}',
                            '476'::jsonb,
                            false)
                        ELSE destination_charge_snapshot_json
                    END,
                updated_at_utc = now()
            WHERE is_active = TRUE
              AND bunker_cost = 280.00
              AND (
                  UPPER(BTRIM(COALESCE(carrier_code, ''))) = 'MAERSK'
                  OR UPPER(BTRIM(COALESCE(carrier_name, ''))) LIKE 'MAERSK%'
              )
              AND UPPER(BTRIM(COALESCE(panama_arrival_port_code, ''))) = 'POE-2026-003'
              AND destination_profile_version = 'MATRIX-LIVE';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data correction is intentionally not reversed: after deployment
        // a user may independently edit the bunker amount. Restoring 280
        // would overwrite that later pricing decision.
    }
}

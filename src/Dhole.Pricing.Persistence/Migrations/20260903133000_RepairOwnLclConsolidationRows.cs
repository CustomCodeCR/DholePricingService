using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260903133000_RepairOwnLclConsolidationRows")]
public sealed class RepairOwnLclConsolidationRows : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."OwnLclConsolidations"
                ADD COLUMN IF NOT EXISTS panama_arrival_port_id uuid,
                ADD COLUMN IF NOT EXISTS panama_arrival_port_name varchar(200),
                ADD COLUMN IF NOT EXISTS panama_arrival_port_code varchar(80),
                ADD COLUMN IF NOT EXISTS pod_id uuid,
                ADD COLUMN IF NOT EXISTS pod_name varchar(200),
                ADD COLUMN IF NOT EXISTS pod_code varchar(80),
                ADD COLUMN IF NOT EXISTS ocean_freight numeric(18,6) DEFAULT 0,
                ADD COLUMN IF NOT EXISTS maximum_cbm numeric(18,6) DEFAULT 50,
                ADD COLUMN IF NOT EXISTS carrier_destination_cost_total numeric(18,6) DEFAULT 912,
                ADD COLUMN IF NOT EXISTS panama_to_cr_cost numeric(18,6) DEFAULT 2140,
                ADD COLUMN IF NOT EXISTS bunker_cost numeric(18,6) DEFAULT 280,
                ADD COLUMN IF NOT EXISTS cr_transfer_base_cbm numeric(18,6) DEFAULT 95,
                ADD COLUMN IF NOT EXISTS matrix_version varchar(80),
                ADD COLUMN IF NOT EXISTS status varchar(40) DEFAULT 'Draft',
                ADD COLUMN IF NOT EXISTS is_active boolean DEFAULT TRUE;

            WITH missing_numbers AS (
                SELECT id,
                       COALESCE((SELECT MAX(consolidation_number)
                                 FROM pricing."OwnLclConsolidations"
                                 WHERE consolidation_number IS NOT NULL), 47)
                       + ROW_NUMBER() OVER (ORDER BY id) AS repaired_number
                FROM pricing."OwnLclConsolidations"
                WHERE consolidation_number IS NULL
            )
            UPDATE pricing."OwnLclConsolidations" target
            SET consolidation_number = missing_numbers.repaired_number
            FROM missing_numbers
            WHERE target.id = missing_numbers.id;

            UPDATE pricing."OwnLclConsolidations"
            SET name = COALESCE(NULLIF(BTRIM(name), ''), 'Consolidado ' || consolidation_number::text),
                pol_code = COALESCE(NULLIF(BTRIM(pol_code), ''), 'SHANGHAI'),
                ocean_freight = COALESCE(ocean_freight, 0),
                maximum_cbm = CASE WHEN maximum_cbm IS NULL OR maximum_cbm <= 0 THEN 50 ELSE maximum_cbm END,
                carrier_destination_cost_total = COALESCE(carrier_destination_cost_total, 912),
                panama_to_cr_cost = COALESCE(panama_to_cr_cost, 2140),
                bunker_cost = COALESCE(bunker_cost, 280),
                cr_transfer_base_cbm = CASE WHEN cr_transfer_base_cbm IS NULL OR cr_transfer_base_cbm <= 0 THEN 95 ELSE cr_transfer_base_cbm END,
                matrix_version = COALESCE(NULLIF(BTRIM(matrix_version), ''), 'CNCA-' || LPAD(consolidation_number::text, 3, '0') || '-v1'),
                status = COALESCE(NULLIF(BTRIM(status), ''), 'Draft'),
                is_active = COALESCE(is_active, TRUE),
                pod_name = COALESCE(NULLIF(BTRIM(pod_name), ''), NULLIF(BTRIM(panama_arrival_port_name), '')),
                pod_code = COALESCE(NULLIF(BTRIM(pod_code), ''), NULLIF(BTRIM(panama_arrival_port_code), ''));

            ALTER TABLE pricing."OwnLclConsolidations"
                ALTER COLUMN consolidation_number SET NOT NULL,
                ALTER COLUMN name SET NOT NULL,
                ALTER COLUMN pol_code SET NOT NULL,
                ALTER COLUMN ocean_freight SET DEFAULT 0,
                ALTER COLUMN ocean_freight SET NOT NULL,
                ALTER COLUMN maximum_cbm SET DEFAULT 50,
                ALTER COLUMN maximum_cbm SET NOT NULL,
                ALTER COLUMN carrier_destination_cost_total SET DEFAULT 912,
                ALTER COLUMN carrier_destination_cost_total SET NOT NULL,
                ALTER COLUMN panama_to_cr_cost SET DEFAULT 2140,
                ALTER COLUMN panama_to_cr_cost SET NOT NULL,
                ALTER COLUMN bunker_cost SET DEFAULT 280,
                ALTER COLUMN bunker_cost SET NOT NULL,
                ALTER COLUMN cr_transfer_base_cbm SET DEFAULT 95,
                ALTER COLUMN cr_transfer_base_cbm SET NOT NULL,
                ALTER COLUMN matrix_version SET NOT NULL,
                ALTER COLUMN status SET DEFAULT 'Draft',
                ALTER COLUMN status SET NOT NULL,
                ALTER COLUMN is_active SET DEFAULT TRUE,
                ALTER COLUMN is_active SET NOT NULL;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data repair is intentionally non-destructive and should not be rolled back.
    }
}

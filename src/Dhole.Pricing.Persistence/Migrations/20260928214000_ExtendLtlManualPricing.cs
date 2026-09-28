using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260928214000_ExtendLtlManualPricing")]
public sealed class ExtendLtlManualPricing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."FtlTariffs"
                ADD COLUMN IF NOT EXISTS cost_per_cbm numeric(18,6) NULL,
                ADD COLUMN IF NOT EXISTS weight_kg_per_cbm numeric(18,2) NULL,
                ADD COLUMN IF NOT EXISTS dua_cost numeric(18,2) NULL,
                ADD COLUMN IF NOT EXISTS duca_t_cost numeric(18,2) NULL,
                ADD COLUMN IF NOT EXISTS stuffing_cost_per_cbm numeric(18,6) NULL,
                ADD COLUMN IF NOT EXISTS stuffing_sale_per_cbm numeric(18,2) NULL,
                ADD COLUMN IF NOT EXISTS panama_cost_surcharge_per_cbm numeric(18,2) NULL;

            -- Base operacional del LTL. Las ventas y mínimos existentes se conservan:
            -- el usuario los sigue digitando/editando desde la matriz LTL.
            UPDATE pricing."FtlTariffs"
            SET
                cost_per_cbm = COALESCE(
                    cost_per_cbm,
                    CASE
                        WHEN lower(translate(destination_name, 'áéíóúüñ', 'aeiouun')) LIKE '%managua%' THEN 2035.0 / 60.0
                        WHEN lower(translate(destination_name, 'áéíóúüñ', 'aeiouun')) LIKE '%san pedro sula%' THEN 2560.0 / 60.0
                        WHEN lower(translate(destination_name, 'áéíóúüñ', 'aeiouun')) LIKE '%san salvador%' THEN 2035.0 / 60.0
                        WHEN lower(translate(destination_name, 'áéíóúüñ', 'aeiouun')) LIKE '%guatemala%' THEN 1800.0 / 60.0
                        ELSE 0
                    END
                ),
                weight_kg_per_cbm = COALESCE(weight_kg_per_cbm, 330),
                dua_cost = COALESCE(dua_cost, 50),
                duca_t_cost = COALESCE(duca_t_cost, 30),
                stuffing_cost_per_cbm = COALESCE(stuffing_cost_per_cbm, 550.0 / 60.0),
                stuffing_sale_per_cbm = COALESCE(stuffing_sale_per_cbm, 10),
                panama_cost_surcharge_per_cbm = COALESCE(panama_cost_surcharge_per_cbm, 9),
                updated_at_utc = NOW()
            WHERE lower(shipment_mode) = 'ltl';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."FtlTariffs"
                DROP COLUMN IF EXISTS panama_cost_surcharge_per_cbm,
                DROP COLUMN IF EXISTS stuffing_sale_per_cbm,
                DROP COLUMN IF EXISTS stuffing_cost_per_cbm,
                DROP COLUMN IF EXISTS duca_t_cost,
                DROP COLUMN IF EXISTS dua_cost,
                DROP COLUMN IF EXISTS weight_kg_per_cbm,
                DROP COLUMN IF EXISTS cost_per_cbm;
            """
        );
    }
}

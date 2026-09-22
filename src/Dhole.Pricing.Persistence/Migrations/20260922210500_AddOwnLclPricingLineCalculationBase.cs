using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260922210500_AddOwnLclPricingLineCalculationBase")]
public sealed class AddOwnLclPricingLineCalculationBase : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."OwnLclConsolidationPricingLines"
                ADD COLUMN IF NOT EXISTS calculation_base_cbm numeric(18,6) NULL;

            -- Los fletes terrestres de Centroamérica antes guardaban costo/CBM.
            -- Desde ahora guardan costo TOTAL + CBM base + venta/CBM.
            UPDATE pricing."OwnLclConsolidationPricingLines"
            SET cost_unit = cost_unit * 70,
                calculation_base_cbm = 70,
                updated_at_utc = now()
            WHERE UPPER(line_key) IN ('CA_INLAND_NI','CA_INLAND_HN','CA_INLAND_GT','CA_INLAND_SV')
              AND calculation_base_cbm IS NULL;

            -- Transbordo: costo destino Panamá/CBM + 9.
            UPDATE pricing."OwnLclConsolidationPricingLines" AS target
            SET cost_unit = (
                    SELECT (c.carrier_destination_cost_total / GREATEST(c.maximum_cbm, 0.01)) + 9
                    FROM pricing."OwnLclConsolidations" AS c
                    WHERE c.id = target.consolidation_id
                ),
                sale_unit = COALESCE((
                    SELECT panama.sale_unit + 9
                    FROM pricing."OwnLclConsolidationPricingLines" AS panama
                    WHERE panama.consolidation_id = target.consolidation_id
                      AND UPPER(panama.line_key) = 'PA_DESTINATION_CHARGE'
                    LIMIT 1
                ), 29),
                updated_at_utc = now()
            WHERE UPPER(target.line_key) = 'CA_TRANSSHIPMENT';

            -- Stuffing: COSTO = USD 550 / 60 CBM. La venta vuelve a ser editable.
            UPDATE pricing."OwnLclConsolidationPricingLines"
            SET cost_unit = 550.0 / 60.0,
                sale_unit = CASE
                    WHEN sale_unit BETWEEN 9.16 AND 9.18 THEN 10
                    ELSE sale_unit
                END,
                updated_at_utc = now()
            WHERE UPPER(line_key) = 'CA_STUFFING';

            UPDATE pricing."OwnLclConsolidationPricingLines"
            SET sale_unit = 185,
                updated_at_utc = now()
            WHERE UPPER(line_key) = 'CA_DOCUMENTATION';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."OwnLclConsolidationPricingLines"
                DROP COLUMN IF EXISTS calculation_base_cbm;
            """
        );
    }
}

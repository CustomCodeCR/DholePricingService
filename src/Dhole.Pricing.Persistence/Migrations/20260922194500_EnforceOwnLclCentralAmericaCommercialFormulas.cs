using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260922194500_EnforceOwnLclCentralAmericaCommercialFormulas")]
public sealed class EnforceOwnLclCentralAmericaCommercialFormulas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Fórmulas comerciales obligatorias de Centroamérica:
            -- Transbordo = Destination Charge Panamá + USD 9
            -- Stuffing = USD 550 / 60 CBM
            -- Documentación = USD 185 / HBL
            UPDATE pricing."OwnLclConsolidationPricingLines" AS target
            SET sale_unit = COALESCE((
                    SELECT panama.sale_unit + 9
                    FROM pricing."OwnLclConsolidationPricingLines" AS panama
                    WHERE panama.consolidation_id = target.consolidation_id
                      AND UPPER(panama.line_key) = 'PA_DESTINATION_CHARGE'
                    LIMIT 1
                ), 29),
                updated_at_utc = now()
            WHERE UPPER(target.line_key) = 'CA_TRANSSHIPMENT';

            UPDATE pricing."OwnLclConsolidationPricingLines"
            SET sale_unit = 550.0 / 60.0,
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
        // Regla comercial vigente; no se revierte para no reintroducir ventas obsoletas.
    }
}

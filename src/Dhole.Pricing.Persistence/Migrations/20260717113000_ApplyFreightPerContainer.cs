using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260717113000_ApplyFreightPerContainer")]
public sealed class ApplyFreightPerContainer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."Costs"
            SET is_accountant = TRUE
            WHERE cost_detail_type IN ('Freight', 'InlandTransport')
              AND is_accountant = FALSE;

            UPDATE pricing."RateDetails" AS detail
            SET quantity = GREATEST(header.container_quantity, 1),
                utility_amount = (detail.sale_amount - detail.cost_amount)
                    * GREATEST(header.container_quantity, 1)
            FROM pricing."RateHeaders" AS header
            WHERE detail.rate_header_id = header.id
              AND detail.cost_detail_type IN ('Freight', 'InlandTransport');

            WITH totals AS (
                SELECT
                    detail.rate_header_id,
                    SUM(detail.cost_amount * detail.quantity) AS total_cost,
                    SUM(detail.sale_amount * detail.quantity) AS total_sale
                FROM pricing."RateDetails" AS detail
                GROUP BY detail.rate_header_id
            )
            UPDATE pricing."RateHeaders" AS header
            SET total_cost_amount = totals.total_cost,
                total_sale_amount = totals.total_sale,
                total_utility_amount = totals.total_sale - totals.total_cost,
                margin_percentage = CASE
                    WHEN totals.total_sale <= 0 THEN 0
                    ELSE ROUND(
                        ((totals.total_sale - totals.total_cost) / totals.total_sale) * 100,
                        2
                    )
                END
            FROM totals
            WHERE header.id = totals.rate_header_id;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Es una corrección de datos de negocio. No se revierte para evitar perder
        // la configuración contable que ya existiera antes de esta migración.
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261007162500_AlignLtlCentroamericaRules")]
public sealed class AlignLtlCentroamericaRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."FtlTariffs"
            SET weight_kg_per_cbm = 333.33,
                updated_at_utc = NOW()
            WHERE lower(shipment_mode) = 'ltl'
              AND (
                  weight_kg_per_cbm IS NULL
                  OR abs(weight_kg_per_cbm - 330) < 0.01
              );
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."FtlTariffs"
            SET weight_kg_per_cbm = 330,
                updated_at_utc = NOW()
            WHERE lower(shipment_mode) = 'ltl'
              AND abs(weight_kg_per_cbm - 333.33) < 0.01;
            """
        );
    }
}

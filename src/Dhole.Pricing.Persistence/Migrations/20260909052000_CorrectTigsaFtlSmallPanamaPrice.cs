using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260909052000_CorrectTigsaFtlSmallPanamaPrice")]
public sealed class CorrectTigsaFtlSmallPanamaPrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."Costs"
            SET cost_amount = 3000,
                sale_amount = CASE WHEN sale_amount = 3300 THEN 3000 ELSE sale_amount END,
                utility_amount = (CASE WHEN sale_amount = 3300 THEN 3000 ELSE sale_amount END) - 3000
            WHERE name = 'FTL TIGSA · San Pedro Sula → Panamá · Equipo 5 a 7 toneladas'
              AND shipment_mode = 'Ftl'
              AND cost_detail_type = 'Freight'
              AND cost_amount = 3300
              AND COALESCE(notes, '') LIKE '%[FTL_EQUIPMENT_CLASS=5_7_TON]%';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data correction intentionally remains in place on rollback.
    }
}

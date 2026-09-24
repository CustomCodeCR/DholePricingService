using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260924180000_ClearFtlTariffsForManualEntry")]
public sealed class ClearFtlTariffsForManualEntry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM pricing."FtlTariffs"
            WHERE lower(COALESCE(shipment_mode, 'Ftl')) = 'ftl';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentional no-op: deleted FTL tariff data is not recreated automatically.
        // FTL tariffs are manually maintained after this migration.
    }
}

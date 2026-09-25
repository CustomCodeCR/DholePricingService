using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260925090000_RepairLandTariffCurrencyBusinessValue")]
public sealed class RepairLandTariffCurrencyBusinessValue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."FtlTariffs"
            SET currency_code = upper(trim(currency_name)),
                updated_at_utc = COALESCE(updated_at_utc, now())
            WHERE currency_code LIKE 'CUR-%'
              AND currency_name IS NOT NULL
              AND trim(currency_name) ~* '^[A-Z]{3}$';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No-op: restoring internal catalog codes into business-facing currency fields is undesirable.
    }
}

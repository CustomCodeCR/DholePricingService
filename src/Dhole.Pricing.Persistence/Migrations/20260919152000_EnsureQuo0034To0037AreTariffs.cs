using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260919152000_EnsureQuo0034To0037AreTariffs")]
public sealed class EnsureQuo0034To0037AreTariffs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."RateHeaders"
            SET rate_type = 'Tariff',
                updated_at_utc = now()
            WHERE (
                    upper(trim(COALESCE(quo_number, ''))) IN (
                        'QUO-00000-000034',
                        'QUO-00000-000035',
                        'QUO-00000-000036',
                        'QUO-00000-000037'
                    )
                    OR upper(trim(COALESCE(rate_code, ''))) IN (
                        'QUO-00000-000034',
                        'QUO-00000-000035',
                        'QUO-00000-000036',
                        'QUO-00000-000037'
                    )
                  )
              AND rate_type <> 'Tariff';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Historical data correction is intentionally not reverted.
    }
}

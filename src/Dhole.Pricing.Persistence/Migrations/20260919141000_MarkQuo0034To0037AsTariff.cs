using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260919141000_MarkQuo0034To0037AsTariff")]
public sealed class MarkQuo0034To0037AsTariff : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."RateHeaders"
            SET rate_type = 'Tariff',
                updated_at_utc = now()
            WHERE quo_number IN (
                'QUO-00000-000034',
                'QUO-00000-000035',
                'QUO-00000-000036',
                'QUO-00000-000037'
            )
              AND rate_type = 'Spot';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentional production data correction: do not revert these QUOs to SPOT.
    }
}

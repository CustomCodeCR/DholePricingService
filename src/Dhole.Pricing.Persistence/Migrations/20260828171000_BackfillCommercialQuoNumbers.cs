using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260828171000_BackfillCommercialQuoNumbers")]
public sealed class BackfillCommercialQuoNumbers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."RateHeaders"
            SET quo_number = rate_code
            WHERE (quo_number IS NULL OR BTRIM(quo_number) = '')
              AND rate_code IS NOT NULL
              AND BTRIM(rate_code) <> '';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data repair migration: do not erase tracking numbers on rollback.
    }
}

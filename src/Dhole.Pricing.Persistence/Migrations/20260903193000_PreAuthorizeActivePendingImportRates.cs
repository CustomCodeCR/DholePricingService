using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260903193000_PreAuthorizeActivePendingImportRates")]
public sealed class PreAuthorizeActivePendingImportRates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."ImportFclRates"
            SET status = 'PreAuthorized',
                updated_at_utc = CURRENT_TIMESTAMP,
                updated_by = COALESCE(updated_by, 'system-preauthorization')
            WHERE status = 'Pending'
              AND is_deleted = FALSE
              AND valid_to >= (date_trunc('day', CURRENT_TIMESTAMP AT TIME ZONE 'UTC') AT TIME ZONE 'UTC');
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data promotion is intentionally non-destructive. We cannot distinguish
        // rows that were already pre-authorized from rows promoted by this migration.
    }
}

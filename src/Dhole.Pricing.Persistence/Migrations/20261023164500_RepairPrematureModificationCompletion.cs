using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261023164500_RepairPrematureModificationCompletion")]
public sealed class RepairPrematureModificationCompletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A seller modification for BRAME was auto-closed when its already-linked
        // original quote changed status. Reopen only this identified work item.
        // Never touch other completed requests or create another RateHeader.
        migrationBuilder.Sql("""
            UPDATE pricing."RateRequests"
               SET status = 'Open',
                   completed_at_utc = NULL,
                   work_status = 'Working',
                   work_status_updated_at_utc = NOW(),
                   locked_by_user_id = NULL,
                   locked_by_name = NULL,
                   lock_token = NULL,
                   locked_at_utc = NULL,
                   lock_expires_at_utc = NULL
             WHERE id = '81c710ae-ff92-4e03-b68b-e9f19070c88d'
               AND status = 'Completed'
               AND work_status = 'Working'
               AND payload_json ->> 'kind' = 'Modification';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally irreversible: rolling back must not re-close a request
        // that Pricing may have edited or completed after this repair.
    }
}

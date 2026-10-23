using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261023183000_ReopenUnattendedBrameModification")]
public sealed class ReopenUnattendedBrameModification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Repair only the specific prematurely closed seller modification.
        // A modification with a prepared revision has actually advanced and
        // must never be reopened by this data repair.
        migrationBuilder.Sql("""
            UPDATE pricing."RateRequests"
               SET status = 'Open',
                   completed_at_utc = NULL,
                   prepared_revision_number = NULL,
                   work_status = 'Working',
                   work_status_updated_at_utc = NOW(),
                   locked_by_user_id = NULL,
                   locked_by_name = NULL,
                   lock_token = NULL,
                   locked_at_utc = NULL,
                   lock_expires_at_utc = NULL
             WHERE id = '81c710ae-ff92-4e03-b68b-e9f19070c88d'
               AND status = 'Completed'
               AND prepared_revision_number IS NULL
               AND payload_json ->> 'kind' = 'Modification';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never automatically mark an attended seller request as completed
        // on downgrade. The repair is intentionally irreversible.
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261106150000_RequireExplicitSellerModificationClosure")]
public sealed class RequireExplicitSellerModificationClosure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Advancing a QUO revision is a draft/save operation, not authorization
            -- to complete an outstanding seller modification.
            DROP TRIGGER IF EXISTS trg_complete_seller_modification_on_revision
                ON pricing."RateHeaders";
            DROP FUNCTION IF EXISTS pricing.complete_seller_modification_on_revision();

            -- Only recover unaudited, orphaned cancellation states. Real cancellations
            -- carry an actor, reason and timestamp and must remain untouched.
            UPDATE pricing."RateRequests"
               SET status = 'Open',
                   completed_at_utc = NULL,
                   work_status = COALESCE(work_status, 'Working'),
                   work_status_updated_at_utc = clock_timestamp(),
                   locked_by_user_id = NULL,
                   locked_by_name = NULL,
                   lock_token = NULL,
                   locked_at_utc = NULL,
                   lock_expires_at_utc = NULL
             WHERE status = 'Cancelled'
               AND payload_json ->> 'kind' = 'Modification'
               AND cancelled_at_utc IS NULL
               AND cancelled_by_user_id IS NULL
               AND cancellation_reason IS NULL;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentional: do NOT reinstall the premature-completion trigger or
        // re-cancel already restored seller work items.
    }
}

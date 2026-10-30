using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261030143000_EnforcePreparedSellerModificationCompletion")]
public sealed class EnforcePreparedSellerModificationCompletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Reassert the post-approval delivery model on every database, including
        // environments that retained the former delivery-before-completion check.
        // The request leaves the dashboard when its saved revision is explicitly
        // sent, even when margin approval or Notifications delivery is pending.
        // Do not infer completion from a changed quote: explicit send is required.
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateRequests"
                DROP CONSTRAINT IF EXISTS ck_rate_requests_modification_requires_delivery;

            ALTER TABLE pricing."RateRequests"
                DROP CONSTRAINT IF EXISTS ck_rate_requests_modification_requires_prepared_revision;

            ALTER TABLE pricing."RateRequests"
                ADD CONSTRAINT ck_rate_requests_modification_requires_prepared_revision
                CHECK (
                    COALESCE(payload_json ->> 'kind', '') <> 'Modification'
                    OR status <> 'Completed'
                    OR (
                        rate_id IS NOT NULL
                        AND prepared_revision_number IS NOT NULL
                        AND prepared_revision_number > 0
                        AND completed_at_utc IS NOT NULL
                    )
                ) NOT VALID;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The obsolete delivery requirement cannot safely be restored: completed
        // requests may still be awaiting approval or durable outbox delivery.
    }
}

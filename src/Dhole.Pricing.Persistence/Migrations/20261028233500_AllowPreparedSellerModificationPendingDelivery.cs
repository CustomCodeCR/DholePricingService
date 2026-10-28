using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261028233500_AllowPreparedSellerModificationPendingDelivery")]
public sealed class AllowPreparedSellerModificationPendingDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Before asynchronous delivery was introduced, this constraint required
        // a seller amendment to have delivery_confirmed_at_utc populated as soon
        // as the request became Completed. That is now incorrect: Pricing closes
        // its work when a saved revision is prepared, and the delivery worker
        // queues the email only after margin approval and PDF generation.
        //
        // Keep the original invoice/QUO, revision and seller email untouched.
        // Completed seller modifications must have a prepared saved revision and
        // a completion timestamp. Delivery confirmation is intentionally optional.
        //
        // NOT VALID preserves historical completed records made before revision
        // preparation was tracked; every new INSERT/UPDATE is still checked.
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateRequests"
                DROP CONSTRAINT IF EXISTS ck_rate_requests_modification_requires_delivery;

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
        // Restoring the old delivery-before-completion constraint would reject
        // intentionally Completed requests awaiting approval/outbox delivery,
        // including existing data. Never revert to that unsafe schema on rollback.
    }
}

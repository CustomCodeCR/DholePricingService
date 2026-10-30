using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261030154000_AutoCompleteSellerModificationOnRevisionAdvance")]
public sealed class AutoCompleteSellerModificationOnRevisionAdvance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Saving the next revision of a quote that has an open seller modification
        // completes Pricing's work, atomically with that saved revision.
        // A seller-requested modification must be linked to this same quote,
        // and its source revision must be the version being replaced.
        // Notification/PDF delivery remains an independent, retryable step.
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION pricing.complete_seller_modification_on_revision()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.revision_number <= OLD.revision_number THEN
                    RETURN NEW;
                END IF;

                UPDATE pricing."RateRequests" request
                   SET status = 'Completed',
                       prepared_revision_number = NEW.revision_number,
                       completed_at_utc = clock_timestamp(),
                       work_status = NULL,
                       work_status_updated_at_utc = NULL,
                       locked_by_user_id = NULL,
                       locked_by_name = NULL,
                       lock_token = NULL,
                       locked_at_utc = NULL,
                       lock_expires_at_utc = NULL
                 WHERE request.rate_id = NEW.id
                   AND request.status = 'Open'
                   AND request.payload_json ->> 'kind' = 'Modification'
                   AND CASE
                         WHEN request.payload_json #>> '{modification,sourceRevisionNumber}' ~ '^[0-9]{1,9}$'
                         THEN (request.payload_json #>> '{modification,sourceRevisionNumber}')::integer
                         ELSE NULL
                       END = OLD.revision_number;
                RETURN NEW;
            END;
            $$;

            DROP TRIGGER IF EXISTS trg_complete_seller_modification_on_revision
                ON pricing."RateHeaders";

            CREATE TRIGGER trg_complete_seller_modification_on_revision
                AFTER UPDATE OF revision_number ON pricing."RateHeaders"
                FOR EACH ROW
                WHEN (NEW.revision_number > OLD.revision_number)
                EXECUTE FUNCTION pricing.complete_seller_modification_on_revision();

            -- Repair stranded historical requests such as a seller revision
            -- saved successfully while the old completion endpoint returned an
            -- error. Require historical evidence: RateRevisions captures the
            -- PREVIOUS revision at the time of the actual update. Only changes
            -- recorded AFTER the request was submitted qualify.
            -- Never touch unrelated new requests, cancelled requests, quotes
            -- without a newer revision, or already completed history.
            UPDATE pricing."RateRequests" request
               SET status = 'Completed',
                   prepared_revision_number = quote.revision_number,
                   completed_at_utc = COALESCE(
                       (SELECT MIN(previous."CreatedAtUtc")
                          FROM pricing."RateRevisions" previous
                         WHERE previous."RateHeaderId" = request.rate_id
                           AND previous."RevisionNumber" = CASE
                               WHEN request.payload_json #>> '{modification,sourceRevisionNumber}' ~ '^[0-9]{1,9}$'
                               THEN (request.payload_json #>> '{modification,sourceRevisionNumber}')::integer
                               ELSE NULL
                           END
                           AND previous."CreatedAtUtc" >= request.requested_at_utc),
                       clock_timestamp()
                   ),
                   work_status = NULL,
                   work_status_updated_at_utc = NULL,
                   locked_by_user_id = NULL,
                   locked_by_name = NULL,
                   lock_token = NULL,
                   locked_at_utc = NULL,
                   lock_expires_at_utc = NULL
              FROM pricing."RateHeaders" quote
             WHERE request.rate_id = quote.id
               AND request.status = 'Open'
               AND request.payload_json ->> 'kind' = 'Modification'
               AND quote.is_deleted = false
               AND CASE
                   WHEN request.payload_json #>> '{modification,sourceRevisionNumber}' ~ '^[0-9]{1,9}$'
                   THEN (request.payload_json #>> '{modification,sourceRevisionNumber}')::integer
                   ELSE NULL
               END < quote.revision_number
               AND EXISTS (
                   SELECT 1 FROM pricing."RateRevisions" previous
                    WHERE previous."RateHeaderId" = quote.id
                      AND previous."RevisionNumber" = CASE
                          WHEN request.payload_json #>> '{modification,sourceRevisionNumber}' ~ '^[0-9]{1,9}$'
                          THEN (request.payload_json #>> '{modification,sourceRevisionNumber}')::integer
                          ELSE NULL
                      END
                      AND previous."CreatedAtUtc" >= request.requested_at_utc
               );
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve closed work items and their audit history on rollback.
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS trg_complete_seller_modification_on_revision
                ON pricing."RateHeaders";
            DROP FUNCTION IF EXISTS pricing.complete_seller_modification_on_revision();
            """
        );
    }
}

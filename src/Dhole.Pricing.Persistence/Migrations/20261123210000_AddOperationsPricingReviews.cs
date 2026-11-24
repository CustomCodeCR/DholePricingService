using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dhole.Pricing.Persistence.Migrations;

/// <summary>Operations requests are durable but never implicitly change Quote rows.</summary>
[DbContext(typeof(ServiceDbContext))]
[Migration("20261123210000_AddOperationsPricingReviews")]
public sealed class AddOperationsPricingReviews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing.pricing_operations_review_requests (
                id uuid NOT NULL CONSTRAINT p_k_operations_review_requests PRIMARY KEY,
                operations_request_id uuid NOT NULL UNIQUE,
                case_id uuid NOT NULL,
                company_id uuid NOT NULL,
                idtra varchar(32) NOT NULL,
                service_id uuid NOT NULL,
                quote_id uuid NULL,
                accepted_option_id uuid NULL,
                service_code varchar(60) NOT NULL,
                reason text NOT NULL,
                status varchar(40) NOT NULL DEFAULT 'PENDING_REVIEW',
                requested_by uuid NOT NULL,
                requested_at_utc timestamptz NOT NULL DEFAULT now(),
                decided_by uuid NULL,
                decided_at_utc timestamptz NULL,
                decision_reason text NULL,
                version bigint NOT NULL DEFAULT 1,
                CONSTRAINT ck_pricing_operations_review_status CHECK (status IN
                    ('PENDING_REVIEW','REVISION_REQUIRED','NO_PRICE_CHANGE_REQUIRED','REJECTED'))
            );
            CREATE INDEX IF NOT EXISTS ix_pricing_operations_review_queue
                ON pricing.pricing_operations_review_requests(status, requested_at_utc);
            CREATE INDEX IF NOT EXISTS ix_pricing_operations_review_case
                ON pricing.pricing_operations_review_requests(case_id);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not drop review requests or their approval evidence on rollback.
    }
}

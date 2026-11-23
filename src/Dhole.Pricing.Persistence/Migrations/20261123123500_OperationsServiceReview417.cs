using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261123123500_OperationsServiceReview417")]
public sealed class OperationsServiceReview417 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing.operations_service_reviews (
                id uuid PRIMARY KEY,
                case_id uuid NOT NULL,
                quote_id uuid NULL,
                quote_group_id uuid NULL,
                service_catalog_id uuid NOT NULL,
                service_code varchar(80) NOT NULL,
                service_name varchar(255) NOT NULL,
                reason text NOT NULL,
                requested_by uuid NOT NULL,
                requested_at_utc timestamptz NOT NULL,
                status varchar(35) NOT NULL DEFAULT 'PENDING_PRICING',
                resolution_reason text NULL,
                resolved_by uuid NULL,
                resolved_at_utc timestamptz NULL
            );
            CREATE INDEX IF NOT EXISTS ix_pricing_service_reviews_status
                ON pricing.operations_service_reviews(status, requested_at_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_pricing_service_reviews_quote
                ON pricing.operations_service_reviews(quote_id, requested_at_utc DESC);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

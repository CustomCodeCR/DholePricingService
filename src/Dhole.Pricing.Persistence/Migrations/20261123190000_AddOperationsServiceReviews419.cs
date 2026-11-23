using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261123190000_AddOperationsServiceReviews419")]
public sealed class AddOperationsServiceReviews419 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing.operations_service_reviews (
                id uuid NOT NULL PRIMARY KEY,
                quote_id uuid NOT NULL,
                case_id uuid NOT NULL,
                idtra varchar(100) NOT NULL,
                service_code varchar(80) NOT NULL,
                service_name varchar(300) NOT NULL,
                reason varchar(2000) NOT NULL,
                requested_by uuid NOT NULL,
                requested_at_utc timestamptz NOT NULL,
                status varchar(16) NOT NULL DEFAULT 'PENDING'
                  CHECK (status IN ('PENDING','APPROVED','REJECTED')),
                resolved_by uuid NULL,
                resolved_at_utc timestamptz NULL,
                resolution_notes varchar(2000) NULL
            );
            CREATE INDEX IF NOT EXISTS ix_pricing_operations_review_queue
              ON pricing.operations_service_reviews(status, requested_at_utc DESC);
            """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retain operations review history and decisions.");
}

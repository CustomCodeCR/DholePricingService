using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

/// <summary>
/// Immutable request reasons and explicit decisions, keyed to a quote revision.
/// Only one pending or approved request is allowed for any given revision.
/// </summary>
[DbContext(typeof(ServiceDbContext))]
[Migration("20261120103000_AddExpiredRateUpdateApprovals")]
public sealed class AddExpiredRateUpdateApprovals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing."ExpiredRateUpdateApprovals" (
              "Id" uuid NOT NULL PRIMARY KEY,
              "RateId" uuid NOT NULL,
              "RevisionNumber" integer NOT NULL,
              "Reason" text NOT NULL,
              "RequestedByUserId" uuid NOT NULL,
              "RequestedAtUtc" timestamptz NOT NULL DEFAULT now(),
              "Status" varchar(16) NOT NULL DEFAULT 'Pending',
              "DecidedByUserId" uuid NULL,
              "DecidedAtUtc" timestamptz NULL,
              "DecisionReason" text NULL,
              CONSTRAINT "CK_ExpiredRateUpdateApprovals_Status"
                CHECK ("Status" IN ('Pending','Approved','Rejected')),
              CONSTRAINT "CK_ExpiredRateUpdateApprovals_Reason"
                CHECK (length(btrim("Reason")) >= 15),
              CONSTRAINT "FK_ExpiredRateUpdateApprovals_Rate"
                FOREIGN KEY ("RateId") REFERENCES pricing."RateHeaders"(id)
            );
            CREATE INDEX IF NOT EXISTS "IX_ExpiredRateUpdateApprovals_RateHistory"
              ON pricing."ExpiredRateUpdateApprovals"
                 ("RateId","RequestedAtUtc" DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_ExpiredRateUpdateApprovals_ActiveRevision"
              ON pricing."ExpiredRateUpdateApprovals" ("RateId","RevisionNumber")
              WHERE "Status" IN ('Pending','Approved');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS pricing."ExpiredRateUpdateApprovals";
            """);
    }
}

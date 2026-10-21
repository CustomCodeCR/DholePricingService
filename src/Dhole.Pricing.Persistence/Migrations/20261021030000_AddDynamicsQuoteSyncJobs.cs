using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261021030000_AddDynamicsQuoteSyncJobs")]
public sealed class AddDynamicsQuoteSyncJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."DynamicsQuoteSyncJobs"
            (
                "Id" uuid NOT NULL PRIMARY KEY,
                "RateId" uuid NOT NULL,
                "RevisionNumber" integer NOT NULL,
                "EventId" uuid NOT NULL,
                "PayloadJson" text NOT NULL,
                "Status" varchar(32) NOT NULL DEFAULT 'Pending',
                "Attempts" integer NOT NULL DEFAULT 0,
                "NextAttemptAtUtc" timestamptz NOT NULL DEFAULT now(),
                "LeaseExpiresAtUtc" timestamptz NULL,
                "LastError" varchar(500) NULL,
                "ResponseJson" text NULL,
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT now(),
                "CompletedAtUtc" timestamptz NULL,
                CONSTRAINT "UX_DynamicsQuoteSyncJobs_RateRevision" UNIQUE ("RateId", "RevisionNumber"),
                CONSTRAINT "UX_DynamicsQuoteSyncJobs_Event" UNIQUE ("EventId"),
                CONSTRAINT "CK_DynamicsQuoteSyncJobs_Status"
                    CHECK ("Status" IN ('Pending','Processing','Retry','Completed','NeedsAttention'))
            );
            CREATE INDEX IF NOT EXISTS "IX_DynamicsQuoteSyncJobs_Due"
            ON pricing."DynamicsQuoteSyncJobs" ("NextAttemptAtUtc", "CreatedAtUtc")
            WHERE "Status" IN ('Pending','Retry','Processing');
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP TABLE IF EXISTS pricing."DynamicsQuoteSyncJobs";""");
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

/// <summary>
/// Durable one-shot marker for each scheduled reminder. Inserted in the same
/// PostgreSQL transaction as the Notifications Outbox event.
/// </summary>
[DbContext(typeof(ServiceDbContext))]
[Migration("20261025140000_AddRateExpiryReminderQueue")]
public sealed class AddRateExpiryReminderQueue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing."RateExpiryReminderQueue" (
                "RateId" uuid NOT NULL,
                "RevisionNumber" integer NOT NULL,
                "ValidToDate" date NOT NULL,
                "Milestone" varchar(4) NOT NULL,
                "QueuedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT "PK_RateExpiryReminderQueue"
                    PRIMARY KEY ("RateId", "RevisionNumber", "ValidToDate", "Milestone"),
                CONSTRAINT "CK_RateExpiryReminderQueue_Milestone"
                    CHECK ("Milestone" IN ('5d', '3d', '1d', '6h'))
            );
            CREATE INDEX IF NOT EXISTS "IX_RateExpiryReminderQueue_QueuedAtUtc"
                ON pricing."RateExpiryReminderQueue" ("QueuedAtUtc");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS pricing."RateExpiryReminderQueue";
            """);
    }
}

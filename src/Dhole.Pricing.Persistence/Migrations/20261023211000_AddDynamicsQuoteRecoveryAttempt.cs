using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261023211000_AddDynamicsQuoteRecoveryAttempt")]
public sealed class AddDynamicsQuoteRecoveryAttempt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."DynamicsQuoteSyncJobs"
            ADD COLUMN IF NOT EXISTS "RecoveryAttempt" integer NOT NULL DEFAULT 0;
            ALTER TABLE pricing."DynamicsQuoteSyncJobs"
            ADD CONSTRAINT "CK_DynamicsQuoteSyncJobs_RecoveryAttempt"
            CHECK ("RecoveryAttempt" BETWEEN 0 AND 3);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."DynamicsQuoteSyncJobs"
            DROP CONSTRAINT IF EXISTS "CK_DynamicsQuoteSyncJobs_RecoveryAttempt";
            ALTER TABLE pricing."DynamicsQuoteSyncJobs"
            DROP COLUMN IF EXISTS "RecoveryAttempt";
            """);
    }
}

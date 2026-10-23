using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261023160000_AddRateRequestWorkStatus")]
public sealed class AddRateRequestWorkStatus : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.Sql("""
            ALTER TABLE pricing."RateRequests"
              ADD COLUMN IF NOT EXISTS work_status character varying(32) NULL,
              ADD COLUMN IF NOT EXISTS work_status_updated_at_utc timestamp with time zone NULL;
            UPDATE pricing."RateRequests"
            SET work_status = 'Working', work_status_updated_at_utc = COALESCE(locked_at_utc, NOW())
            WHERE status = 'Open' AND locked_by_user_id IS NOT NULL
              AND lock_expires_at_utc > NOW() AND work_status IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder builder)
    {
        builder.Sql("""
            ALTER TABLE pricing."RateRequests"
              DROP COLUMN IF EXISTS work_status,
              DROP COLUMN IF EXISTS work_status_updated_at_utc;
            """);
    }
}

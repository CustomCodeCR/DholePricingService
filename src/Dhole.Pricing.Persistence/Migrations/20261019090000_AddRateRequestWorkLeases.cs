using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261019090000_AddRateRequestWorkLeases")]
public sealed class AddRateRequestWorkLeases : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.Sql("""
            ALTER TABLE pricing."RateRequests"
              ADD COLUMN IF NOT EXISTS locked_by_user_id uuid NULL,
              ADD COLUMN IF NOT EXISTS locked_by_name character varying(200) NULL,
              ADD COLUMN IF NOT EXISTS lock_token uuid NULL,
              ADD COLUMN IF NOT EXISTS locked_at_utc timestamp with time zone NULL,
              ADD COLUMN IF NOT EXISTS lock_expires_at_utc timestamp with time zone NULL;
            CREATE INDEX IF NOT EXISTS "IX_RateRequests_lock_expires_at_utc"
              ON pricing."RateRequests" (lock_expires_at_utc);
            """);
    }

    protected override void Down(MigrationBuilder builder)
    {
        builder.Sql("""
            DROP INDEX IF EXISTS pricing."IX_RateRequests_lock_expires_at_utc";
            ALTER TABLE pricing."RateRequests"
              DROP COLUMN IF EXISTS locked_by_user_id,
              DROP COLUMN IF EXISTS locked_by_name,
              DROP COLUMN IF EXISTS lock_token,
              DROP COLUMN IF EXISTS locked_at_utc,
              DROP COLUMN IF EXISTS lock_expires_at_utc;
            """);
    }
}

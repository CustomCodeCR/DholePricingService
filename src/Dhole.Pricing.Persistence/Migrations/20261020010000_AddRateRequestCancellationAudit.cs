using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261020010000_AddRateRequestCancellationAudit")]
public sealed class AddRateRequestCancellationAudit : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.Sql("""
            ALTER TABLE pricing."RateRequests"
              ADD COLUMN IF NOT EXISTS cancelled_at_utc timestamp with time zone NULL,
              ADD COLUMN IF NOT EXISTS cancelled_by_user_id uuid NULL,
              ADD COLUMN IF NOT EXISTS cancelled_by_name character varying(200) NULL,
              ADD COLUMN IF NOT EXISTS cancellation_reason character varying(40) NULL,
              ADD COLUMN IF NOT EXISTS cancellation_details character varying(500) NULL;
            """);
    }

    protected override void Down(MigrationBuilder builder)
    {
        builder.Sql("""
            ALTER TABLE pricing."RateRequests"
              DROP COLUMN IF EXISTS cancelled_at_utc,
              DROP COLUMN IF EXISTS cancelled_by_user_id,
              DROP COLUMN IF EXISTS cancelled_by_name,
              DROP COLUMN IF EXISTS cancellation_reason,
              DROP COLUMN IF EXISTS cancellation_details;
            """);
    }
}

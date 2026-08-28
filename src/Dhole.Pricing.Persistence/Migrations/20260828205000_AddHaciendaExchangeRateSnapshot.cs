using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260828205000_AddHaciendaExchangeRateSnapshot")]
public sealed class AddHaciendaExchangeRateSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateHeaders"
                ADD COLUMN IF NOT EXISTS exchange_rate_purchase numeric(18,6),
                ADD COLUMN IF NOT EXISTS exchange_rate_sale numeric(18,6),
                ADD COLUMN IF NOT EXISTS exchange_rate_applied numeric(18,6),
                ADD COLUMN IF NOT EXISTS exchange_rate_date timestamp with time zone,
                ADD COLUMN IF NOT EXISTS exchange_rate_captured_at_utc timestamp with time zone,
                ADD COLUMN IF NOT EXISTS exchange_rate_source character varying(160),
                ADD COLUMN IF NOT EXISTS exchange_rate_manual_override boolean NOT NULL DEFAULT FALSE;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateHeaders"
                DROP COLUMN IF EXISTS exchange_rate_manual_override,
                DROP COLUMN IF EXISTS exchange_rate_source,
                DROP COLUMN IF EXISTS exchange_rate_captured_at_utc,
                DROP COLUMN IF EXISTS exchange_rate_date,
                DROP COLUMN IF EXISTS exchange_rate_applied,
                DROP COLUMN IF EXISTS exchange_rate_sale,
                DROP COLUMN IF EXISTS exchange_rate_purchase;
            """
        );
    }
}

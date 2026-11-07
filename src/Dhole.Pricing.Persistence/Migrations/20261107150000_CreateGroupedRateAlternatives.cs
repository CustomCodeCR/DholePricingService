using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261107150000_CreateGroupedRateAlternatives")]
public sealed class CreateGroupedRateAlternatives : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing.rate_quote_alternatives (
                rate_id uuid PRIMARY KEY REFERENCES pricing."RateHeaders"(id) ON DELETE CASCADE,
                parent_rate_id uuid NOT NULL REFERENCES pricing."RateHeaders"(id) ON DELETE CASCADE,
                base_quote varchar(16) NOT NULL,
                alternative_quote varchar(100) NOT NULL,
                suffix varchar(12) NOT NULL,
                poe_id uuid NOT NULL,
                pod_id uuid NULL,
                carrier_id uuid NOT NULL,
                haulage_mode varchar(12) NOT NULL CHECK (haulage_mode IN ('Merchant', 'Carrier')),
                route_key text NOT NULL DEFAULT '',
                status varchar(16) NOT NULL DEFAULT 'Draft'
                    CHECK (status IN ('Draft', 'Selected', 'NotSelected')),
                created_at_utc timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT ux_quote_group_suffix UNIQUE(parent_rate_id, suffix),
                CONSTRAINT ux_quote_group_route UNIQUE(parent_rate_id, poe_id, carrier_id, haulage_mode, route_key),
                CONSTRAINT ux_quote_group_number UNIQUE(alternative_quote)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_quote_group_selected
                ON pricing.rate_quote_alternatives(parent_rate_id) WHERE status = 'Selected';
            CREATE INDEX IF NOT EXISTS ix_quote_group_parent
                ON pricing.rate_quote_alternatives(parent_rate_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS pricing.rate_quote_alternatives;");
    }
}

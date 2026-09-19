using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260919070000_AddTariffApplicationSource")]
public sealed class AddTariffApplicationSource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."RateHeaders"
                ADD COLUMN IF NOT EXISTS source_tariff_rate_id uuid NULL;

            ALTER TABLE pricing."RateHeaders"
                ADD COLUMN IF NOT EXISTS source_tariff_revision_number integer NULL;

            CREATE INDEX IF NOT EXISTS ix_rate_headers_source_tariff_rate_id
                ON pricing."RateHeaders" (source_tariff_rate_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS pricing.ix_rate_headers_source_tariff_rate_id;

            ALTER TABLE pricing."RateHeaders"
                DROP COLUMN IF EXISTS source_tariff_revision_number;

            ALTER TABLE pricing."RateHeaders"
                DROP COLUMN IF EXISTS source_tariff_rate_id;
            """);
    }
}

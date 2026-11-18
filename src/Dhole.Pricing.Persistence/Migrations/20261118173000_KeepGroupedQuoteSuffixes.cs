using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

/// <summary>
/// A commercial MultiQUO owns one consecutive, and every option retains its
/// immutable suffix after selection. Reconcile older selected 672 rows without
/// deleting, renumbering, or changing internal RateCode audit references.
/// </summary>
[DbContext(typeof(ServiceDbContext))]
[Migration("20261118173000_KeepGroupedQuoteSuffixes")]
public sealed class KeepGroupedQuoteSuffixes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."RateHeaders"
                DROP CONSTRAINT IF EXISTS ck_rate_headers_rate_code_format;
            ALTER TABLE pricing."RateHeaders"
                ALTER COLUMN rate_code TYPE varchar(32);
            ALTER TABLE pricing."RateHeaders"
                ADD CONSTRAINT ck_rate_headers_rate_code_format
                CHECK (rate_code ~ '^QUO-[A-Z0-9]{5}-[A-Z0-9]{6}(-[A-Z]+)?$');

            -- Previously selected alternatives had their suffix erased.
            -- The recorded base/suffix pair is authoritative for every child.
            UPDATE pricing.rate_quote_alternatives a
            SET alternative_quote = a.base_quote || '-' || a.suffix
            WHERE a.alternative_quote IS DISTINCT FROM a.base_quote || '-' || a.suffix;

            UPDATE pricing."RateHeaders" h
            SET quo_number = a.base_quote || '-' || a.suffix
            FROM pricing.rate_quote_alternatives a
            WHERE h.id = a.rate_id
              AND h.quo_number IS DISTINCT FROM a.base_quote || '-' || a.suffix;
            """);
    }

    // Old commercial numbers might already have been delivered externally.
    // Automatic rollback to unsuffixed numbers would corrupt those references.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

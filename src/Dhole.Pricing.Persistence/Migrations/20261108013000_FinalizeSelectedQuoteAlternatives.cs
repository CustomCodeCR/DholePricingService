using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

/// <summary>
/// Promote already selected FCL sub-QUOs to their unsuffixed commercial number.
/// Keep other alternatives and their original suffixes for audit/history.
/// </summary>
[DbContext(typeof(ServiceDbContext))]
[Migration("20261108013000_FinalizeSelectedQuoteAlternatives")]
public sealed class FinalizeSelectedQuoteAlternatives : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE pricing."RateHeaders" h
            SET quo_number = a.base_quote
            FROM pricing.rate_quote_alternatives a
            WHERE h.id = a.rate_id
              AND a.status = 'Selected'
              AND h.quo_number IS DISTINCT FROM a.base_quote
              AND NOT EXISTS (
                  SELECT 1 FROM pricing.rate_quote_alternatives other
                  JOIN pricing."RateHeaders" accepted ON accepted.id = other.rate_id
                  WHERE other.parent_rate_id = a.parent_rate_id
                    AND other.rate_id <> a.rate_id
                    AND accepted.status = 'AcceptedByClient'
              );

            UPDATE pricing.rate_quote_alternatives a
            SET alternative_quote = a.base_quote
            WHERE a.status = 'Selected'
              AND a.alternative_quote IS DISTINCT FROM a.base_quote
              AND EXISTS (
                  SELECT 1 FROM pricing."RateHeaders" h
                  WHERE h.id = a.rate_id AND h.quo_number = a.base_quote
              );
            """);
    }

    // This migration finalizes a commercial customer decision.
    // Do not revert customer-facing numbering through an automatic Down migration.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

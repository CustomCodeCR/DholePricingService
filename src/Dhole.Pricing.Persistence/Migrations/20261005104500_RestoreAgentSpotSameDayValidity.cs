using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261005104500_RestoreAgentSpotSameDayValidity")]
public sealed class RestoreAgentSpotSameDayValidity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."ImportFclRates"
            SET valid_to = valid_from,
                status = CASE
                    WHEN valid_from::date < CURRENT_DATE
                         AND status IN ('Pending', 'PreAuthorized', 'Approved')
                    THEN 'Expired'
                    WHEN valid_from::date >= CURRENT_DATE
                         AND status = 'Expired'
                    THEN 'PreAuthorized'
                    ELSE status
                END,
                updated_at_utc = CURRENT_TIMESTAMP,
                updated_by = COALESCE(updated_by, 'system-agent-spot-validity')
            WHERE source_type = 'AgentExtraction'
              AND is_deleted = FALSE
              AND (
                    valid_to <> valid_from
                 OR (
                        valid_from::date < CURRENT_DATE
                        AND status IN ('Pending', 'PreAuthorized', 'Approved')
                    )
                 OR (
                        valid_from::date >= CURRENT_DATE
                        AND status = 'Expired'
                    )
              );
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The original Agent payload does not define a multi-day validity window.
        // Reverting this data correction would reintroduce invalid SPOT validity.
    }
}

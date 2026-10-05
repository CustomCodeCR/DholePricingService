using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261005091500_RepairAgentExtractionValidity")]
public sealed class RepairAgentExtractionValidity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            WITH candidates AS (
                SELECT
                    id,
                    GREATEST(
                        valid_from,
                        COALESCE(
                            NULLIF(raw_data_json->'offer'->>'cargoCutoff', '')::timestamptz,
                            NULLIF(raw_data_json->'offer'->>'etd', '')::timestamptz,
                            valid_from + INTERVAL '7 days'
                        )
                    ) AS repaired_valid_to
                FROM pricing."ImportFclRates"
                WHERE source_type = 'AgentExtraction'
                  AND is_deleted = FALSE
                  AND valid_to <= valid_from
            )
            UPDATE pricing."ImportFclRates" rates
            SET valid_to = candidates.repaired_valid_to,
                status = CASE
                    WHEN rates.status = 'Expired'
                     AND (candidates.repaired_valid_to AT TIME ZONE 'America/Costa_Rica')::date
                         >= (CURRENT_TIMESTAMP AT TIME ZONE 'America/Costa_Rica')::date
                    THEN 'PreAuthorized'
                    ELSE rates.status
                END,
                updated_at_utc = CURRENT_TIMESTAMP,
                updated_by = COALESCE(rates.updated_by, 'system-agent-validity-repair')
            FROM candidates
            WHERE rates.id = candidates.id;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data repair is intentionally non-destructive.
    }
}

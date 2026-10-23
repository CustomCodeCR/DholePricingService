using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261023190000_ReopenBrameUnsentModification")]
public sealed class ReopenBrameUnsentModification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Operator reported this exact completion as accidental on leaving the tab.
        // Only repair that recorded completion of this one amendment. Leave the
        // other BRAME request (92dc4a82-...) and the linked quote unchanged.
        migrationBuilder.Sql("""
            UPDATE pricing."RateRequests"
               SET status = 'Open',
                   completed_at_utc = NULL,
                   work_status = 'Working',
                   work_status_updated_at_utc = NOW(),
                   locked_by_user_id = NULL,
                   locked_by_name = NULL,
                   lock_token = NULL,
                   locked_at_utc = NULL,
                   lock_expires_at_utc = NULL
             WHERE id = '81c710ae-ff92-4e03-b68b-e9f19070c88d'
               AND status = 'Completed'
               AND completed_at_utc = TIMESTAMPTZ '2026-10-23 12:11:10.655-06'
               AND payload_json ->> 'kind' = 'Modification'
               AND payload_json -> 'modification' ->> 'quote' = 'QUO-00000-000336';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restoring an accidentally completed status would lose seller work.
    }
}

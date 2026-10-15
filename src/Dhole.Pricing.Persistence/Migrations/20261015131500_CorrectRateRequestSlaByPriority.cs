using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261015131500_CorrectRateRequestSlaByPriority")]
public sealed class CorrectRateRequestSlaByPriority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing open requests must use the new deadlines as well, otherwise the
        // UI and the background SLA worker continue using the former inverted values.
        // Completed/cancelled requests and the original requested timestamp are preserved.
        migrationBuilder.Sql(
            """
            UPDATE pricing."RateRequests"
            SET due_at_utc = requested_at_utc + CASE priority
                    WHEN 'Red' THEN INTERVAL '24 hours'
                    WHEN 'Green' THEN INTERVAL '72 hours'
                END,
                -- Green used to expire after 24h; allow a reminder at its new 72h deadline.
                sla_reminder_sent_at_utc = CASE
                    WHEN priority = 'Green' THEN NULL
                    ELSE sla_reminder_sent_at_utc
                END
            WHERE status = 'Open' AND priority IN ('Red', 'Green');
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not silently rewrite historical SLA/notification state on rollback.
    }
}

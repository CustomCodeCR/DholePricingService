using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260903203000_AddSellerRateRequests")]
public sealed class AddSellerRateRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."RateRequests" (
                id uuid NOT NULL,
                priority character varying(20) NOT NULL,
                status character varying(20) NOT NULL,
                requested_at_utc timestamp with time zone NOT NULL,
                due_at_utc timestamp with time zone NOT NULL,
                completed_at_utc timestamp with time zone NULL,
                sla_reminder_sent_at_utc timestamp with time zone NULL,
                rate_id uuid NULL,
                seller_user_id uuid NULL,
                seller_name character varying(200) NULL,
                seller_email character varying(320) NULL,
                client_name character varying(250) NULL,
                executive_name character varying(200) NULL,
                shipment_mode character varying(32) NULL,
                origin_name character varying(250) NULL,
                destination_name character varying(250) NULL,
                payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
                CONSTRAINT "PK_RateRequests" PRIMARY KEY (id)
            );

            CREATE INDEX IF NOT EXISTS "IX_RateRequests_status_priority_due_requested"
                ON pricing."RateRequests" (status, priority, due_at_utc, requested_at_utc);

            CREATE INDEX IF NOT EXISTS "IX_RateRequests_rate_id"
                ON pricing."RateRequests" (rate_id);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS pricing.\"RateRequests\";");
    }
}

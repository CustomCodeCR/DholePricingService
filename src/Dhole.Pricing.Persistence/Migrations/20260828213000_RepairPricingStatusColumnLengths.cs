using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260828213000_RepairPricingStatusColumnLengths")]
public sealed class RepairPricingStatusColumnLengths : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'pricing'
                      AND table_name = 'RateHeaders'
                      AND column_name = 'status'
                ) THEN
                    ALTER TABLE pricing."RateHeaders"
                    ALTER COLUMN status TYPE character varying(50)
                    USING status::character varying(50);
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'pricing'
                      AND table_name = 'ImportFclRates'
                      AND column_name = 'status'
                ) THEN
                    ALTER TABLE pricing."ImportFclRates"
                    ALTER COLUMN status TYPE character varying(50)
                    USING status::character varying(50);
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'pricing'
                      AND table_name = 'PricingImportFromExtractionJobs'
                      AND column_name = 'status'
                ) THEN
                    ALTER TABLE pricing."PricingImportFromExtractionJobs"
                    ALTER COLUMN status TYPE character varying(50)
                    USING status::character varying(50);
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'pricing'
                      AND table_name = 'inbox_messages'
                      AND column_name = 'status'
                ) THEN
                    ALTER TABLE pricing.inbox_messages
                    ALTER COLUMN status TYPE character varying(50)
                    USING status::character varying(50);
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'pricing'
                      AND table_name = 'outbox_messages'
                      AND column_name = 'status'
                ) THEN
                    ALTER TABLE pricing.outbox_messages
                    ALTER COLUMN status TYPE character varying(50)
                    USING status::character varying(50);
                END IF;
            END $$;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally no-op. Current Pricing statuses exceed 10 characters,
        // so shrinking these columns would be destructive and reintroduce 22001.
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260904023000_AddRateComparisons")]
public sealed class AddRateComparisons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."RateComparisons" (
                id uuid NOT NULL,
                source_import_fcl_rate_id uuid NOT NULL,
                compared_rate_header_id uuid NOT NULL,
                compared_rate_code character varying(80) NOT NULL,
                comparison_type character varying(40) NOT NULL,
                status character varying(30) NOT NULL,
                pol_id uuid NOT NULL,
                pol_name character varying(160) NOT NULL,
                poe_id uuid NOT NULL,
                poe_name character varying(160) NOT NULL,
                container_type_id uuid NOT NULL,
                container_type_name character varying(120) NOT NULL,
                currency_code character varying(20) NOT NULL,
                baseline_cost_amount numeric(18,2) NOT NULL,
                baseline_sale_amount numeric(18,2) NOT NULL,
                candidate_cost_amount numeric(18,2) NOT NULL,
                candidate_sale_amount numeric(18,2) NOT NULL,
                baseline_compared_amount numeric(18,2) NOT NULL,
                candidate_compared_amount numeric(18,2) NOT NULL,
                savings_amount numeric(18,2) NOT NULL,
                savings_percent numeric(9,4) NOT NULL,
                candidate_payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
                created_rate_header_id uuid NULL,
                created_at_utc timestamp with time zone NOT NULL,
                resolved_at_utc timestamp with time zone NULL,
                resolved_by uuid NULL,
                CONSTRAINT "PK_RateComparisons" PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS pricing."RateComparisonDetails" (
                id uuid NOT NULL,
                rate_comparison_id uuid NOT NULL,
                cost_id uuid NULL,
                name character varying(250) NOT NULL,
                cost_detail_type character varying(50) NOT NULL,
                cost_type character varying(50) NOT NULL,
                charge_basis character varying(40) NOT NULL,
                currency_code character varying(20) NOT NULL,
                quantity numeric(18,6) NOT NULL,
                baseline_cost_amount numeric(18,2) NOT NULL,
                baseline_sale_amount numeric(18,2) NOT NULL,
                candidate_cost_amount numeric(18,2) NOT NULL,
                candidate_sale_amount numeric(18,2) NOT NULL,
                notes text NULL,
                CONSTRAINT "PK_RateComparisonDetails" PRIMARY KEY (id),
                CONSTRAINT "FK_RateComparisonDetails_RateComparisons_rate_comparison_id"
                    FOREIGN KEY (rate_comparison_id)
                    REFERENCES pricing."RateComparisons" (id)
                    ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RateComparisons_source_baseline_type"
                ON pricing."RateComparisons" (source_import_fcl_rate_id, compared_rate_header_id, comparison_type);

            CREATE INDEX IF NOT EXISTS "IX_RateComparisons_status_created"
                ON pricing."RateComparisons" (status, created_at_utc);

            CREATE INDEX IF NOT EXISTS "IX_RateComparisons_route_container"
                ON pricing."RateComparisons" (pol_id, poe_id, container_type_id);

            CREATE INDEX IF NOT EXISTS "IX_RateComparisonDetails_rate_comparison_id"
                ON pricing."RateComparisonDetails" (rate_comparison_id);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS pricing."RateComparisonDetails";
            DROP TABLE IF EXISTS pricing."RateComparisons";
            """
        );
    }
}

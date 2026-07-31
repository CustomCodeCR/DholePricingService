using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260730160200_AddExtractionImportJobs")]
public sealed class AddExtractionImportJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PricingImportFromExtractionJobs",
            schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                external_request_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                email_extraction_job_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                extraction_execution_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                pricing_import_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                payload_json = table.Column<string>(
                    type: "jsonb",
                    nullable: false
                ),
                correlation_id = table.Column<string>(
                    type: "character varying(150)",
                    maxLength: 150,
                    nullable: false
                ),
                status = table.Column<string>(
                    type: "character varying(50)",
                    maxLength: 50,
                    nullable: false
                ),
                attempt_count = table.Column<int>(
                    type: "integer",
                    nullable: false,
                    defaultValue: 0
                ),
                max_attempt_count = table.Column<int>(
                    type: "integer",
                    nullable: false,
                    defaultValue: 3
                ),
                next_attempt_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                lease_owner = table.Column<string>(
                    type: "character varying(250)",
                    maxLength: 250,
                    nullable: true
                ),
                lease_expires_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                error_code = table.Column<string>(
                    type: "character varying(250)",
                    maxLength: 250,
                    nullable: true
                ),
                error_message = table.Column<string>(
                    type: "character varying(4000)",
                    maxLength: 4000,
                    nullable: true
                ),
                persisted_rows = table.Column<int>(
                    type: "integer",
                    nullable: false,
                    defaultValue: 0
                ),
                skipped_rows = table.Column<int>(
                    type: "integer",
                    nullable: false,
                    defaultValue: 0
                ),
                started_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                completed_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                version = table.Column<int>(
                    type: "integer",
                    nullable: false,
                    defaultValue: 1
                ),
                created_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                created_by = table.Column<string>(
                    type: "text",
                    nullable: true
                ),
                updated_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                updated_by = table.Column<string>(
                    type: "text",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "p_k_pricing_import_from_extraction_jobs",
                    x => x.id
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "i_x_pricing_import_from_extraction_jobs_external_request_id",
            schema: "pricing",
            table: "PricingImportFromExtractionJobs",
            column: "external_request_id",
            unique: true
        );
        migrationBuilder.CreateIndex(
            name: "i_x_pricing_import_from_extraction_jobs_email_extraction_job_id",
            schema: "pricing",
            table: "PricingImportFromExtractionJobs",
            column: "email_extraction_job_id"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_pricing_import_from_extraction_jobs_extraction_execution_id",
            schema: "pricing",
            table: "PricingImportFromExtractionJobs",
            column: "extraction_execution_id"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_pricing_import_from_extraction_jobs_pricing_import_id",
            schema: "pricing",
            table: "PricingImportFromExtractionJobs",
            column: "pricing_import_id"
        );
        migrationBuilder.CreateIndex(
            name: "ix_pricing_extraction_jobs_queue",
            schema: "pricing",
            table: "PricingImportFromExtractionJobs",
            columns: ["status", "next_attempt_at_utc", "created_at_utc"]
        );
        migrationBuilder.CreateIndex(
            name: "ix_pricing_extraction_jobs_lease",
            schema: "pricing",
            table: "PricingImportFromExtractionJobs",
            columns: ["status", "lease_expires_at_utc"]
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PricingImportFromExtractionJobs",
            schema: "pricing"
        );
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261015193000_AddManualImportExtractionJobs")]
public sealed class AddManualImportExtractionJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ManualImportExtractionJobs", schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                requested_by_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                client_request_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                original_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                profile_slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                file_content = table.Column<byte[]>(type: "bytea", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                lease_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                result_json = table.Column<string>(type: "jsonb", nullable: true),
                error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
            },
            constraints: table => table.PrimaryKey("pk_manual_import_extraction_jobs", x => x.id)
        );
        migrationBuilder.CreateIndex(
            name: "ix_manual_import_jobs_request_key", schema: "pricing",
            table: "ManualImportExtractionJobs",
            columns: ["requested_by", "client_request_key"],
            unique: true,
            filter: "client_request_key IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "ix_manual_import_jobs_queue", schema: "pricing",
            table: "ManualImportExtractionJobs",
            columns: ["status", "lease_expires_at_utc", "created_at_utc"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ManualImportExtractionJobs", schema: "pricing");
    }
}

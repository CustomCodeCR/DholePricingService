using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DiagnosticModelDiff20261021 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DynamicsQuoteSyncJobs",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs");

            migrationBuilder.RenameColumn(
                name: "Status",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Attempts",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "attempts");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RevisionNumber",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "revision_number");

            migrationBuilder.RenameColumn(
                name: "ResponseJson",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "response_json");

            migrationBuilder.RenameColumn(
                name: "RateId",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "rate_id");

            migrationBuilder.RenameColumn(
                name: "PayloadJson",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "payload_json");

            migrationBuilder.RenameColumn(
                name: "NextAttemptAtUtc",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "next_attempt_at_utc");

            migrationBuilder.RenameColumn(
                name: "LeaseExpiresAtUtc",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "lease_expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "LastError",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "last_error");

            migrationBuilder.RenameColumn(
                name: "EventId",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "event_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "CompletedAtUtc",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "completed_at_utc");

            migrationBuilder.AddPrimaryKey(
                name: "p_k_dynamics_quote_sync_jobs",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "p_k_dynamics_quote_sync_jobs",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs");

            migrationBuilder.RenameColumn(
                name: "status",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "attempts",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "Attempts");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "revision_number",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "RevisionNumber");

            migrationBuilder.RenameColumn(
                name: "response_json",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "ResponseJson");

            migrationBuilder.RenameColumn(
                name: "rate_id",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "RateId");

            migrationBuilder.RenameColumn(
                name: "payload_json",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "PayloadJson");

            migrationBuilder.RenameColumn(
                name: "next_attempt_at_utc",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "NextAttemptAtUtc");

            migrationBuilder.RenameColumn(
                name: "lease_expires_at_utc",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "LeaseExpiresAtUtc");

            migrationBuilder.RenameColumn(
                name: "last_error",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "LastError");

            migrationBuilder.RenameColumn(
                name: "event_id",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "EventId");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "completed_at_utc",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                newName: "CompletedAtUtc");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DynamicsQuoteSyncJobs",
                schema: "pricing",
                table: "DynamicsQuoteSyncJobs",
                column: "Id");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLogisticsNewsAndSyncModel20260828 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LogisticsNews",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    SourceCountry = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SourceOffice = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AiSummary = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    AiAnalysisJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AiConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    MatchedRateCount = table.Column<int>(type: "integer", nullable: false),
                    AppliedRateCount = table.Column<int>(type: "integer", nullable: false),
                    LastProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogisticsNews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogisticsNewsRateImpacts",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LogisticsNewsId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportFclRateId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    AppliedComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogisticsNewsRateImpacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsNews_IsActive",
                schema: "pricing",
                table: "LogisticsNews",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsNews_ReceivedAtUtc",
                schema: "pricing",
                table: "LogisticsNews",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsNews_Status",
                schema: "pricing",
                table: "LogisticsNews",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LogisticsNewsRateImpacts_ImportFclRateId",
                schema: "pricing",
                table: "LogisticsNewsRateImpacts",
                column: "ImportFclRateId");

            migrationBuilder.CreateIndex(
                name: "ux_logistics_news_rate_impacts_news_rate",
                schema: "pricing",
                table: "LogisticsNewsRateImpacts",
                columns: new[] { "LogisticsNewsId", "ImportFclRateId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogisticsNews",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "LogisticsNewsRateImpacts",
                schema: "pricing");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRateRevisions20260829 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "revision_number",
                schema: "pricing",
                table: "RateHeaders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "RateRevisions",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RateHeaderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RateName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IdtraNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    QuoNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TotalSaleUsd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalSaleCrc = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MarginPercentage = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RateRevisions_RateHeaders_RateHeaderId",
                        column: x => x.RateHeaderId,
                        principalSchema: "pricing",
                        principalTable: "RateHeaders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RateRevisions_CreatedAtUtc",
                schema: "pricing",
                table: "RateRevisions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ux_rate_revisions_header_number",
                schema: "pricing",
                table: "RateRevisions",
                columns: new[] { "RateHeaderId", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RateRevisions",
                schema: "pricing");

            migrationBuilder.DropColumn(
                name: "revision_number",
                schema: "pricing",
                table: "RateHeaders");
        }
    }
}

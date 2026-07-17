using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260716180000_CompleteRateCommercialFields")]
public sealed class CompleteRateCommercialFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "client_name",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(250)",
            maxLength: 250,
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "idtra_number",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "quo_number",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "includes",
            schema: "pricing",
            table: "RateHeaders",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "subject_to",
            schema: "pricing",
            table: "RateHeaders",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "excludes",
            schema: "pricing",
            table: "RateHeaders",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "transit_days",
            schema: "pricing",
            table: "RateHeaders",
            type: "integer",
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE pricing."RateHeaders"
            SET status = CASE status
                WHEN 'Send' THEN 'Sent'
                WHEN 'AcceptedForClient' THEN 'AcceptedByClient'
                WHEN 'RejectedForClient' THEN 'RejectedByClient'
                ELSE status
            END;
            """
        );

        migrationBuilder.CreateIndex(
            name: "IX_RateHeaders_idtra_number",
            schema: "pricing",
            table: "RateHeaders",
            column: "idtra_number"
        );

        migrationBuilder.CreateIndex(
            name: "IX_RateHeaders_quo_number",
            schema: "pricing",
            table: "RateHeaders",
            column: "quo_number"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RateHeaders_idtra_number",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.DropIndex(
            name: "IX_RateHeaders_quo_number",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.Sql(
            """
            UPDATE pricing."RateHeaders"
            SET status = CASE status
                WHEN 'Sent' THEN 'Send'
                WHEN 'AcceptedByClient' THEN 'AcceptedForClient'
                WHEN 'RejectedByClient' THEN 'RejectedForClient'
                ELSE status
            END;
            """
        );

        migrationBuilder.DropColumn(name: "client_name", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "idtra_number", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "quo_number", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "includes", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "subject_to", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "excludes", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "transit_days", schema: "pricing", table: "RateHeaders");
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260804114500_AddRateClosedAndRequestedStatuses")]
public sealed class AddRateClosedAndRequestedStatuses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "closed_at_utc",
            schema: "pricing",
            table: "RateHeaders",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "closed_by",
            schema: "pricing",
            table: "RateHeaders",
            type: "uuid",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "closed_reason",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "closed_at_utc",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.DropColumn(
            name: "closed_by",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.DropColumn(
            name: "closed_reason",
            schema: "pricing",
            table: "RateHeaders"
        );
    }
}

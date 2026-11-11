using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261111165000_AddRateLinkedAcceptedQuote")]
public sealed class AddRateLinkedAcceptedQuote : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "linked_accepted_rate_id",
            schema: "pricing",
            table: "RateHeaders",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RateHeaders_linked_accepted_rate_id",
            schema: "pricing",
            table: "RateHeaders",
            column: "linked_accepted_rate_id");

        migrationBuilder.AddForeignKey(
            name: "FK_RateHeaders_linked_accepted_rate_id",
            schema: "pricing",
            table: "RateHeaders",
            column: "linked_accepted_rate_id",
            principalSchema: "pricing",
            principalTable: "RateHeaders",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_RateHeaders_linked_accepted_rate_id",
            schema: "pricing",
            table: "RateHeaders");

        migrationBuilder.DropIndex(
            name: "IX_RateHeaders_linked_accepted_rate_id",
            schema: "pricing",
            table: "RateHeaders");

        migrationBuilder.DropColumn(
            name: "linked_accepted_rate_id",
            schema: "pricing",
            table: "RateHeaders");
    }
}

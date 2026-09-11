using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260911033000_AddRateDetailBillToClient")]
public sealed class AddRateDetailBillToClient : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "bill_to_client",
            schema: "pricing",
            table: "RateDetails",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "bill_to_client",
            schema: "pricing",
            table: "RateDetails"
        );
    }
}

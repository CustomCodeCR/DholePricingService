using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260914212000_AddRateComments")]
public sealed class AddRateComments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RateComments",
            schema: "pricing",
            columns: table => new
            {
                RateId = table.Column<Guid>(type: "uuid", nullable: false),
                Comments = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RateComments", x => x.RateId);
                table.ForeignKey(
                    name: "FK_RateComments_RateHeaders_RateId",
                    column: x => x.RateId,
                    principalSchema: "pricing",
                    principalTable: "RateHeaders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "RateComments", schema: "pricing");
    }
}

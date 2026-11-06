using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261106190000_AddRateInlandServiceLocations")]
public sealed class AddRateInlandServiceLocations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."RateHeaders"
                ADD COLUMN IF NOT EXISTS inland_service_locations_json jsonb;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."RateHeaders"
                DROP COLUMN IF EXISTS inland_service_locations_json;
            """);
    }
}

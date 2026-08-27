using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260827235500_AddRatePickupLocation")]
public sealed class AddRatePickupLocation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateHeaders"
                ADD COLUMN IF NOT EXISTS pickup_address character varying(1000),
                ADD COLUMN IF NOT EXISTS pickup_latitude numeric(10,7),
                ADD COLUMN IF NOT EXISTS pickup_longitude numeric(10,7);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateHeaders"
                DROP COLUMN IF EXISTS pickup_longitude,
                DROP COLUMN IF EXISTS pickup_latitude,
                DROP COLUMN IF EXISTS pickup_address;
            """
        );
    }
}

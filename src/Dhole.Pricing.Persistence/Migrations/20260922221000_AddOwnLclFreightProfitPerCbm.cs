using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260922221000_AddOwnLclFreightProfitPerCbm")]
public sealed class AddOwnLclFreightProfitPerCbm : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."OwnLclConsolidations"
                ADD COLUMN IF NOT EXISTS freight_profit_per_cbm numeric(18,6) NOT NULL DEFAULT 5.69;

            UPDATE pricing."OwnLclConsolidations"
            SET freight_profit_per_cbm = 5.69
            WHERE freight_profit_per_cbm IS NULL OR freight_profit_per_cbm < 0;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."OwnLclConsolidations"
                DROP COLUMN IF EXISTS freight_profit_per_cbm;
            """
        );
    }
}

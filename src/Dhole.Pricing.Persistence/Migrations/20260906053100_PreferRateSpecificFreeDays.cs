using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260906053100_PreferRateSpecificFreeDays")]
public sealed class PreferRateSpecificFreeDays : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- El valor enviado por la tarifa/importación seleccionada debe ser la fuente real.
            -- Se desactivan defaults históricos que podían reemplazar 5/7/otros días informados por naviera.
            UPDATE pricing."CarrierFreeDayRules"
            SET is_active = FALSE,
                updated_at_utc = NOW()
            WHERE is_active = TRUE;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No se reactivan reglas de forma masiva porque no se puede distinguir cuáles
        // estaban vigentes intencionalmente antes de esta migración.
    }
}

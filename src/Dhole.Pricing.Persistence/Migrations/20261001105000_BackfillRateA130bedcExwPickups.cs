using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261001105000_BackfillRateA130bedcExwPickups")]
public sealed class BackfillRateA130bedcExwPickups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."RateHeaders"
            SET pickup_locations_json = $$[
              {
                "Address": "Almacén Fiscal Tical, Zona Franca Saret.",
                "Latitude": 9.932771,
                "Longitude": -84.079614,
                "CargoCondition": "FiscalCargo"
              },
              {
                "Address": "Almacén Fiscal Cail, La Uruca.",
                "Latitude": 9.945931,
                "Longitude": -84.091187,
                "CargoCondition": "FiscalCargo"
              },
              {
                "Address": "Comercial Capresso, Guadalupe.",
                "Latitude": 9.932771,
                "Longitude": -84.079614,
                "CargoCondition": "NationalizedCargo"
              }
            ]$$::jsonb,
                pickup_address = 'Almacén Fiscal Tical, Zona Franca Saret.',
                pickup_latitude = 9.932771,
                pickup_longitude = -84.079614,
                updated_at_utc = now()
            WHERE id = 'a130bedc-009a-4b12-8a54-2ab0a93f31a1'::uuid
              AND (pickup_locations_json IS NULL OR pickup_locations_json = '[]'::jsonb);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data repair for one affected quotation is intentionally not reverted.
    }
}

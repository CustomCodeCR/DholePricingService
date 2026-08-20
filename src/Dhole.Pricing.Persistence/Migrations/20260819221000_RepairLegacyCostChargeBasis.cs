using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260819221000_RepairLegacyCostChargeBasis")]
public partial class RepairLegacyCostChargeBasis : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Repair databases where AddShipmentModesAndChargeBasis was already applied.
        // Legacy is_accountant=true meant that the cost was charged per equipment unit.
        migrationBuilder.Sql("""
            UPDATE pricing."Costs"
            SET shipment_mode = COALESCE(shipment_mode, 'Fcl'),
                charge_basis = CASE
                    WHEN shipment_mode = 'Ftl' THEN 'PerTruck'
                    ELSE 'PerContainer'
                END
            WHERE is_accountant = true
              AND charge_basis = 'PerShipment';
            """);

        // Legacy "Único" documentation costs are one BL/document. Other legacy
        // one-time costs correctly remain PerShipment.
        migrationBuilder.Sql("""
            UPDATE pricing."Costs"
            SET charge_basis = 'PerDocument'
            WHERE is_accountant = false
              AND cost_detail_type = 'Documentation'
              AND charge_basis = 'PerShipment';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This migration repairs legacy data. Reverting the semantic correction
        // would be destructive, so Down intentionally leaves the repaired values.
    }
}

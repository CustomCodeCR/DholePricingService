using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260805195500_RepairImportFclRateNumericPrecision")]
public sealed class RepairImportFclRateNumericPrecision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Reassert the precision used by the current model. This repairs databases
        // initialized from an older schema and safely neutralizes malformed legacy
        // numbers instead of allowing them to block the migration with SQLSTATE 22003.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regclass('pricing."ImportFclRates"') IS NOT NULL THEN
                    ALTER TABLE pricing."ImportFclRates"
                        ALTER COLUMN freight TYPE numeric(18,4)
                            USING CASE WHEN abs(freight) <= 99999999999999.9999
                                THEN freight::numeric(18,4) ELSE 0::numeric(18,4) END,
                        ALTER COLUMN ocean_freight TYPE numeric(18,4)
                            USING CASE WHEN ocean_freight IS NULL OR abs(ocean_freight) <= 99999999999999.9999
                                THEN ocean_freight::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN origin_charges TYPE numeric(18,4)
                            USING CASE WHEN origin_charges IS NULL OR abs(origin_charges) <= 99999999999999.9999
                                THEN origin_charges::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN destination_charges TYPE numeric(18,4)
                            USING CASE WHEN destination_charges IS NULL OR abs(destination_charges) <= 99999999999999.9999
                                THEN destination_charges::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN surcharges TYPE numeric(18,4)
                            USING CASE WHEN surcharges IS NULL OR abs(surcharges) <= 99999999999999.9999
                                THEN surcharges::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN total_cost TYPE numeric(18,4)
                            USING CASE WHEN total_cost IS NULL OR abs(total_cost) <= 99999999999999.9999
                                THEN total_cost::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN total_sale TYPE numeric(18,4)
                            USING CASE WHEN total_sale IS NULL OR abs(total_sale) <= 99999999999999.9999
                                THEN total_sale::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN profit TYPE numeric(18,4)
                            USING CASE WHEN profit IS NULL OR abs(profit) <= 99999999999999.9999
                                THEN profit::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN margin TYPE numeric(18,4)
                            USING CASE WHEN margin IS NULL OR abs(margin) <= 99999999999999.9999
                                THEN margin::numeric(18,4) ELSE NULL END;
                END IF;
            END $$;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The historical precision varied by environment. Narrowing these columns
        // during rollback could discard valid imported amounts, so no destructive
        // downgrade is performed.
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260924173000_AddFtlTariffEquipmentApplicability")]
public sealed class AddFtlTariffEquipmentApplicability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."FtlTariffs"
                ADD COLUMN IF NOT EXISTS applicable_equipment_classes text NULL;

            UPDATE pricing."FtlTariffs"
            SET applicable_equipment_classes = jsonb_build_array(upper(trim(equipment_class)))::text
            WHERE NULLIF(trim(COALESCE(applicable_equipment_classes, '')), '') IS NULL
              AND NULLIF(trim(COALESCE(equipment_class, '')), '') IS NOT NULL;

            CREATE INDEX IF NOT EXISTS "IX_FtlTariffs_applicable_equipment_classes"
                ON pricing."FtlTariffs"
                USING gin ((applicable_equipment_classes::jsonb));
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS pricing."IX_FtlTariffs_applicable_equipment_classes";

            ALTER TABLE pricing."FtlTariffs"
                DROP COLUMN IF EXISTS applicable_equipment_classes;
            """
        );
    }
}

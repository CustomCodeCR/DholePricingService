using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260923130000_AddLandLtlCommercialProfiles")]
public sealed class AddLandLtlCommercialProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."FtlTariffs"
                ADD COLUMN IF NOT EXISTS commercial_profile character varying(24) NOT NULL DEFAULT 'General';

            UPDATE pricing."FtlTariffs"
            SET commercial_profile = CASE
                WHEN lower(shipment_mode) = 'ltl' THEN 'FinalClient'
                ELSE 'General'
            END
            WHERE commercial_profile IS NULL
               OR trim(commercial_profile) = ''
               OR (lower(shipment_mode) = 'ltl' AND lower(commercial_profile) = 'general');

            DROP INDEX IF EXISTS pricing."UX_FtlTariffs_route_equipment_name";

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_FtlTariffs_route_equipment_profile_name"
                ON pricing."FtlTariffs"
                (lower(origin_name), lower(destination_name), equipment_class, lower(commercial_profile));

            CREATE INDEX IF NOT EXISTS "IX_FtlTariffs_mode_profile_active"
                ON pricing."FtlTariffs" (shipment_mode, commercial_profile, is_active);

            ALTER TABLE pricing."FtlTariffs"
                DROP CONSTRAINT IF EXISTS ck_ftl_tariffs_commercial_profile;

            ALTER TABLE pricing."FtlTariffs"
                ADD CONSTRAINT ck_ftl_tariffs_commercial_profile
                CHECK (
                    (lower(shipment_mode) = 'ltl' AND lower(commercial_profile) IN ('finalclient', 'nvocc'))
                    OR
                    (lower(shipment_mode) = 'ftl' AND lower(commercial_profile) = 'general')
                );
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM pricing."FtlTariffs"
            WHERE lower(shipment_mode) = 'ltl'
              AND lower(commercial_profile) = 'nvocc';

            DROP INDEX IF EXISTS pricing."IX_FtlTariffs_mode_profile_active";
            DROP INDEX IF EXISTS pricing."UX_FtlTariffs_route_equipment_profile_name";

            ALTER TABLE pricing."FtlTariffs"
                DROP CONSTRAINT IF EXISTS ck_ftl_tariffs_commercial_profile,
                DROP COLUMN IF EXISTS commercial_profile;

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_FtlTariffs_route_equipment_name"
                ON pricing."FtlTariffs" (lower(origin_name), lower(destination_name), equipment_class);
            """
        );
    }
}

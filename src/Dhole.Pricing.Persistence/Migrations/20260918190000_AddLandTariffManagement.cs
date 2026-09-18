using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260918190000_AddLandTariffManagement")]
public sealed class AddLandTariffManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."FtlTariffs"
                ADD COLUMN IF NOT EXISTS shipment_mode character varying(8) NOT NULL DEFAULT 'Ftl',
                ADD COLUMN IF NOT EXISTS rate_basis character varying(32) NOT NULL DEFAULT 'PerTruck',
                ADD COLUMN IF NOT EXISTS minimum_amount numeric(18,2) NULL,
                ADD COLUMN IF NOT EXISTS warehouse_name character varying(250) NULL,
                ADD COLUMN IF NOT EXISTS valid_from date NULL,
                ADD COLUMN IF NOT EXISTS valid_to date NULL;

            UPDATE pricing."FtlTariffs"
            SET shipment_mode = 'Ftl',
                rate_basis = 'PerTruck'
            WHERE shipment_mode IS NULL
               OR trim(shipment_mode) = ''
               OR rate_basis IS NULL
               OR trim(rate_basis) = '';

            ALTER TABLE pricing."FtlTariffs"
                DROP CONSTRAINT IF EXISTS ck_ftl_tariffs_minimum_nonnegative;

            ALTER TABLE pricing."FtlTariffs"
                ADD CONSTRAINT ck_ftl_tariffs_minimum_nonnegative
                CHECK (minimum_amount IS NULL OR minimum_amount >= 0);

            CREATE INDEX IF NOT EXISTS "IX_FtlTariffs_shipment_mode_active"
                ON pricing."FtlTariffs" (shipment_mode, is_active);

            CREATE INDEX IF NOT EXISTS "IX_FtlTariffs_validity"
                ON pricing."FtlTariffs" (valid_from, valid_to);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS pricing."IX_FtlTariffs_validity";
            DROP INDEX IF EXISTS pricing."IX_FtlTariffs_shipment_mode_active";

            ALTER TABLE pricing."FtlTariffs"
                DROP CONSTRAINT IF EXISTS ck_ftl_tariffs_minimum_nonnegative,
                DROP COLUMN IF EXISTS valid_to,
                DROP COLUMN IF EXISTS valid_from,
                DROP COLUMN IF EXISTS warehouse_name,
                DROP COLUMN IF EXISTS minimum_amount,
                DROP COLUMN IF EXISTS rate_basis,
                DROP COLUMN IF EXISTS shipment_mode;
            """
        );
    }
}

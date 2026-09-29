using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260929170000_AddFtlTariffRouteApplicability")]
public sealed class AddFtlTariffRouteApplicability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."FtlTariffs"
                ADD COLUMN IF NOT EXISTS applicable_origin_ids text NULL,
                ADD COLUMN IF NOT EXISTS applicable_destination_ids text NULL;

            UPDATE pricing."FtlTariffs"
            SET applicable_origin_ids = CASE
                    WHEN origin_id IS NULL THEN '[]'
                    ELSE jsonb_build_array(origin_id::text)::text
                END
            WHERE NULLIF(trim(COALESCE(applicable_origin_ids, '')), '') IS NULL;

            UPDATE pricing."FtlTariffs"
            SET applicable_destination_ids = CASE
                    WHEN destination_id IS NULL THEN '[]'
                    ELSE jsonb_build_array(destination_id::text)::text
                END
            WHERE NULLIF(trim(COALESCE(applicable_destination_ids, '')), '') IS NULL;

            CREATE INDEX IF NOT EXISTS "IX_FtlTariffs_applicable_origin_ids"
                ON pricing."FtlTariffs"
                USING gin ((applicable_origin_ids::jsonb));

            CREATE INDEX IF NOT EXISTS "IX_FtlTariffs_applicable_destination_ids"
                ON pricing."FtlTariffs"
                USING gin ((applicable_destination_ids::jsonb));
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS pricing."IX_FtlTariffs_applicable_origin_ids";
            DROP INDEX IF EXISTS pricing."IX_FtlTariffs_applicable_destination_ids";

            ALTER TABLE pricing."FtlTariffs"
                DROP COLUMN IF EXISTS applicable_origin_ids,
                DROP COLUMN IF EXISTS applicable_destination_ids;
            """
        );
    }
}

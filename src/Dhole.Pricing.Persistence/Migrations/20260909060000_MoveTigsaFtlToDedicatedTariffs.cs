using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260909060000_MoveTigsaFtlToDedicatedTariffs")]
public sealed class MoveTigsaFtlToDedicatedTariffs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."FtlTariffs"
            (
                id uuid NOT NULL PRIMARY KEY,
                origin_id uuid NULL,
                origin_name character varying(250) NOT NULL,
                origin_code character varying(80) NULL,
                destination_id uuid NULL,
                destination_name character varying(250) NOT NULL,
                destination_code character varying(80) NULL,
                equipment_class character varying(32) NOT NULL,
                equipment_label character varying(100) NOT NULL,
                currency_id uuid NOT NULL,
                currency_name character varying(250) NOT NULL,
                currency_code character varying(80) NOT NULL,
                price_amount numeric(18,2) NOT NULL,
                transit_days integer NULL,
                source character varying(250) NULL,
                notes text NULL,
                is_active boolean NOT NULL DEFAULT TRUE,
                created_at_utc timestamp with time zone NOT NULL DEFAULT now(),
                updated_at_utc timestamp with time zone NULL,
                CONSTRAINT ck_ftl_tariffs_price_nonnegative CHECK (price_amount >= 0),
                CONSTRAINT ck_ftl_tariffs_transit_nonnegative CHECK (transit_days IS NULL OR transit_days >= 0)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_FtlTariffs_route_equipment_name"
                ON pricing."FtlTariffs" (lower(origin_name), lower(destination_name), equipment_class);

            CREATE INDEX IF NOT EXISTS "IX_FtlTariffs_route_equipment_id"
                ON pricing."FtlTariffs" (origin_id, destination_id, equipment_class);

            CREATE INDEX IF NOT EXISTS "IX_FtlTariffs_active"
                ON pricing."FtlTariffs" (is_active);

            INSERT INTO pricing."FtlTariffs"
            (
                id,
                origin_id,
                origin_name,
                origin_code,
                destination_id,
                destination_name,
                destination_code,
                equipment_class,
                equipment_label,
                currency_id,
                currency_name,
                currency_code,
                price_amount,
                transit_days,
                source,
                notes,
                is_active,
                created_at_utc
            )
            SELECT
                c.id,
                c.pol_id,
                COALESCE(c.pol_name, 'Origen'),
                c.pol_code,
                c.poe_id,
                COALESCE(c.poe_name, 'Destino'),
                c.poe_code,
                substring(c.notes from '\[FTL_EQUIPMENT_CLASS=([^\]]+)\]'),
                CASE substring(c.notes from '\[FTL_EQUIPMENT_CLASS=([^\]]+)\]')
                    WHEN '48_53' THEN 'Equipo 48/53 pies'
                    WHEN '5_7_TON' THEN 'Equipo 5 a 7 toneladas'
                    ELSE 'Equipo FTL'
                END,
                c.currency_id,
                c.currency_name,
                c.currency_code,
                c.cost_amount,
                CASE
                    WHEN c.notes ~ '\[TRANSIT_DAYS=[0-9]+\]'
                        THEN substring(c.notes from '\[TRANSIT_DAYS=([0-9]+)\]')::integer
                    ELSE NULL
                END,
                'TIGSA',
                c.notes,
                c.is_active,
                now()
            FROM pricing."Costs" c
            WHERE c.is_deleted = FALSE
              AND c.shipment_mode = 'Ftl'
              AND c.cost_detail_type = 'Freight'
              AND c.name LIKE 'FTL TIGSA ·%'
              AND c.notes LIKE '%[FTL_EQUIPMENT_CLASS=%'
            ON CONFLICT DO NOTHING;

            UPDATE pricing."Costs"
            SET is_deleted = TRUE,
                is_active = FALSE
            WHERE is_deleted = FALSE
              AND shipment_mode = 'Ftl'
              AND cost_detail_type = 'Freight'
              AND name LIKE 'FTL TIGSA ·%'
              AND notes LIKE '%[FTL_EQUIPMENT_CLASS=%';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."Costs"
            SET is_deleted = FALSE,
                is_active = TRUE
            WHERE shipment_mode = 'Ftl'
              AND cost_detail_type = 'Freight'
              AND name LIKE 'FTL TIGSA ·%'
              AND notes LIKE '%[FTL_EQUIPMENT_CLASS=%';

            DROP TABLE IF EXISTS pricing."FtlTariffs";
            """
        );
    }
}

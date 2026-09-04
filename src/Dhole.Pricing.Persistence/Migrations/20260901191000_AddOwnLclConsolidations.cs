using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260901191000_AddOwnLclConsolidations")]
public sealed class AddOwnLclConsolidations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."OwnLclConsolidations" (
                id uuid PRIMARY KEY,
                consolidation_number integer NOT NULL UNIQUE,
                name varchar(160) NOT NULL,
                booking varchar(120),
                etd date,
                carrier_id uuid,
                carrier_name varchar(200),
                carrier_code varchar(80),
                container_id uuid,
                container_name varchar(160),
                container_code varchar(80),
                pol_id uuid,
                pol_name varchar(200),
                pol_code varchar(80) NOT NULL,
                ocean_freight numeric(18,6) NOT NULL DEFAULT 0,
                maximum_cbm numeric(18,6) NOT NULL DEFAULT 50,
                carrier_destination_cost_total numeric(18,6) NOT NULL DEFAULT 912,
                panama_to_cr_cost numeric(18,6) NOT NULL DEFAULT 2140,
                bunker_cost numeric(18,6) NOT NULL DEFAULT 280,
                cr_transfer_base_cbm numeric(18,6) NOT NULL DEFAULT 95,
                matrix_version varchar(80) NOT NULL,
                status varchar(40) NOT NULL DEFAULT 'Draft',
                is_active boolean NOT NULL DEFAULT TRUE,
                created_at_utc timestamptz NOT NULL DEFAULT now(),
                updated_at_utc timestamptz
            );

            CREATE TABLE IF NOT EXISTS pricing."OwnLclHistoricalRates" (
                id uuid PRIMARY KEY,
                consolidation_number integer NOT NULL,
                destination_code varchar(80) NOT NULL,
                pol_code varchar(80) NOT NULL,
                sale_per_cbm numeric(18,6) NOT NULL,
                valid_to date,
                version varchar(80) NOT NULL,
                UNIQUE (consolidation_number, destination_code, pol_code)
            );

            CREATE INDEX IF NOT EXISTS "IX_OwnLclConsolidations_ETD"
                ON pricing."OwnLclConsolidations" (etd DESC);
            CREATE INDEX IF NOT EXISTS "IX_OwnLclHistoricalRates_Lookup"
                ON pricing."OwnLclHistoricalRates" (consolidation_number, destination_code, pol_code);

            INSERT INTO pricing."OwnLclConsolidations"
                (id, consolidation_number, name, booking, etd, container_name, container_code, pol_name, pol_code,
                 ocean_freight, maximum_cbm, matrix_version, status, is_active)
            VALUES
                ('c4800000-0000-4000-8000-000000000048'::uuid, 48, 'Consolidado 48', NULL, DATE '2026-09-11', '40 NOR', '40NOR', 'Shanghai, China', 'SHANGHAI', 7950, 50, 'CNCA-023-048-v4.6', 'Open', TRUE),
                ('c4900000-0000-4000-8000-000000000049'::uuid, 49, 'Consolidado 49', NULL, DATE '2026-09-18', '40 HC', '40HC', 'Shanghai, China', 'SHANGHAI', 8030, 50, 'CNCA-024-049-v4.7', 'Open', TRUE)
            ON CONFLICT (consolidation_number) DO NOTHING;

            WITH rates(consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version) AS (
                VALUES
                (48,'CR','SHANGHAI',208,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','NINGBO',260,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','QINGDAO',260,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','XIAMEN',265,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','SHANTOU',265,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','DALIAN',265,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','CHONGQING',265,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','FUZHOU',265,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','SHENZHEN',270,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','XINGANG',270,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','SHEKOU',270,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'CR','GUANGZHOU',270,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','SHANGHAI',162,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','NINGBO',214,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','QINGDAO',214,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','XIAMEN',219,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','SHANTOU',219,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','DALIAN',219,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','CHONGQING',219,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','FUZHOU',219,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','SHENZHEN',224,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','XINGANG',224,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','SHEKOU',224,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (48,'PA','GUANGZHOU',224,DATE '2026-09-11','CNCA-023-048-v4.6'),
                (49,'CR','SHANGHAI',210,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','NINGBO',262,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','QINGDAO',262,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','XIAMEN',267,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','SHANTOU',267,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','DALIAN',267,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','CHONGQING',267,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','FUZHOU',267,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','SHENZHEN',272,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','XINGANG',272,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','SHEKOU',272,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'CR','GUANGZHOU',272,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','SHANGHAI',164,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','NINGBO',216,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','QINGDAO',216,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','XIAMEN',221,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','SHANTOU',221,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','DALIAN',221,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','CHONGQING',221,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','FUZHOU',221,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','SHENZHEN',226,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','XINGANG',226,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','SHEKOU',226,DATE '2026-09-18','CNCA-024-049-v4.7'),
                (49,'PA','GUANGZHOU',226,DATE '2026-09-18','CNCA-024-049-v4.7')
            )
            INSERT INTO pricing."OwnLclHistoricalRates"
                (id, consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version)
            SELECT gen_random_uuid(), consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version
            FROM rates
            ON CONFLICT (consolidation_number, destination_code, pol_code) DO NOTHING;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS pricing."OwnLclHistoricalRates";
            DROP TABLE IF EXISTS pricing."OwnLclConsolidations";
            """
        );
    }
}

using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260902213000_EnsureOwnLclShanghaiBaseMatrix")]
public sealed class EnsureOwnLclShanghaiBaseMatrix : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Los consolidados propios de China siempre tienen base marítima
            -- Shanghai -> Balboa. El POL solicitado (Ningbo, Qingdao, etc.)
            -- se resuelve mediante la matriz de diferencial por CBM y no crea
            -- un consolidado independiente por cada puerto.
            INSERT INTO pricing."OwnLclConsolidations"
                (id, consolidation_number, name, booking, etd,
                 carrier_name, carrier_code,
                 container_name, container_code,
                 pol_name, pol_code,
                 panama_arrival_port_name, panama_arrival_port_code,
                 ocean_freight, maximum_cbm, carrier_destination_cost_total,
                 panama_to_cr_cost, bunker_cost, cr_transfer_base_cbm,
                 matrix_version, status, is_active, created_at_utc, updated_at_utc)
            VALUES
                ('c4800000-0000-4000-8000-000000000048'::uuid, 48, 'Consolidado 48', '276206793', DATE '2026-09-11',
                 'Maersk', 'MSK', '40 NOR', '40NOR', 'Shanghai, China', 'SHANGHAI',
                 'Balboa, Panamá', 'BALBOA', 7950, 50, 912, 2140, 280, 95,
                 'CNCA-023-048-v4.6', 'Open', TRUE, now(), now()),
                ('c4900000-0000-4000-8000-000000000049'::uuid, 49, 'Consolidado 49', '276464177', DATE '2026-09-18',
                 'Maersk', 'MSK', '40 HC', '40HC', 'Shanghai, China', 'SHANGHAI',
                 'Balboa, Panamá', 'BALBOA', 8030, 50, 912, 2140, 280, 95,
                 'CNCA-024-049-v4.7', 'Open', TRUE, now(), now())
            ON CONFLICT (consolidation_number)
            DO UPDATE SET
                name = EXCLUDED.name,
                booking = EXCLUDED.booking,
                etd = EXCLUDED.etd,
                carrier_name = EXCLUDED.carrier_name,
                carrier_code = EXCLUDED.carrier_code,
                container_name = EXCLUDED.container_name,
                container_code = EXCLUDED.container_code,
                pol_name = 'Shanghai, China',
                pol_code = 'SHANGHAI',
                panama_arrival_port_name = 'Balboa, Panamá',
                panama_arrival_port_code = 'BALBOA',
                ocean_freight = EXCLUDED.ocean_freight,
                maximum_cbm = EXCLUDED.maximum_cbm,
                carrier_destination_cost_total = EXCLUDED.carrier_destination_cost_total,
                panama_to_cr_cost = 2140,
                bunker_cost = COALESCE(pricing."OwnLclConsolidations".bunker_cost, 280),
                cr_transfer_base_cbm = 95,
                matrix_version = EXCLUDED.matrix_version,
                status = 'Open',
                is_active = TRUE,
                updated_at_utc = now();

            WITH rates(consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version) AS (
                VALUES
                    (48,'CR','SHANGHAI',208::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','NINGBO',260::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','QINGDAO',260::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','XIAMEN',265::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','SHANTOU',265::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','DALIAN',265::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','CHONGQING',265::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','FUZHOU',265::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','SHENZHEN',270::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','XINGANG',270::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','SHEKOU',270::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'CR','GUANGZHOU',270::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','SHANGHAI',162::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','NINGBO',214::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','QINGDAO',214::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','XIAMEN',219::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','SHANTOU',219::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','DALIAN',219::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','CHONGQING',219::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','FUZHOU',219::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','SHENZHEN',224::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','XINGANG',224::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','SHEKOU',224::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (48,'PA','GUANGZHOU',224::numeric,DATE '2026-09-11','CNCA-023-048-v4.6'),
                    (49,'CR','SHANGHAI',210::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','NINGBO',262::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','QINGDAO',262::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','XIAMEN',267::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','SHANTOU',267::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','DALIAN',267::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','CHONGQING',267::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','FUZHOU',267::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','SHENZHEN',272::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','XINGANG',272::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','SHEKOU',272::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'CR','GUANGZHOU',272::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','SHANGHAI',164::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','NINGBO',216::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','QINGDAO',216::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','XIAMEN',221::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','SHANTOU',221::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','DALIAN',221::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','CHONGQING',221::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','FUZHOU',221::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','SHENZHEN',226::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','XINGANG',226::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','SHEKOU',226::numeric,DATE '2026-09-18','CNCA-024-049-v4.7'),
                    (49,'PA','GUANGZHOU',226::numeric,DATE '2026-09-18','CNCA-024-049-v4.7')
            )
            INSERT INTO pricing."OwnLclHistoricalRates"
                (id, consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version)
            SELECT gen_random_uuid(), consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version
            FROM rates
            ON CONFLICT (consolidation_number, destination_code, pol_code)
            DO UPDATE SET
                sale_per_cbm = EXCLUDED.sale_per_cbm,
                valid_to = EXCLUDED.valid_to,
                version = EXCLUDED.version;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Repair/backfill migration: intentionally no destructive rollback.
    }
}

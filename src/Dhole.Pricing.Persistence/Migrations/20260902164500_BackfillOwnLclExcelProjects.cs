using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260902164500_BackfillOwnLclExcelProjects")]
public sealed class BackfillOwnLclExcelProjects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."OwnLclConsolidations"
            SET booking = CASE consolidation_number
                    WHEN 48 THEN '276206793'
                    WHEN 49 THEN '276464177'
                    ELSE booking
                END,
                carrier_name = COALESCE(carrier_name, 'Maersk'),
                carrier_code = COALESCE(carrier_code, 'MSK'),
                panama_arrival_port_name = COALESCE(panama_arrival_port_name, 'Balboa, Panamá'),
                panama_arrival_port_code = COALESCE(panama_arrival_port_code, 'BALBOA'),
                panama_to_cr_cost = 2140,
                bunker_cost = COALESCE(bunker_cost, 280),
                cr_transfer_base_cbm = 95,
                updated_at_utc = now()
            WHERE consolidation_number IN (48, 49);

            WITH corrected(consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version) AS (
                VALUES
                    (48, 'CR', 'SHANGHAI', 208::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'NINGBO', 260::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'QINGDAO', 260::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'XIAMEN', 265::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'SHANTOU', 265::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'DALIAN', 265::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'CHONGQING', 265::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'FUZHOU', 265::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'SHENZHEN', 270::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'XINGANG', 270::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'SHEKOU', 270::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'CR', 'GUANGZHOU', 270::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'SHANGHAI', 162::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'NINGBO', 214::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'QINGDAO', 214::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'XIAMEN', 219::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'SHANTOU', 219::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'DALIAN', 219::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'CHONGQING', 219::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'FUZHOU', 219::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'SHENZHEN', 224::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'XINGANG', 224::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'SHEKOU', 224::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (48, 'PA', 'GUANGZHOU', 224::numeric, DATE '2026-09-11', 'CNCA-023-048-v4.6'),
                    (49, 'CR', 'SHANGHAI', 210::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'NINGBO', 262::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'QINGDAO', 262::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'XIAMEN', 267::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'SHANTOU', 267::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'DALIAN', 267::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'CHONGQING', 267::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'FUZHOU', 267::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'SHENZHEN', 272::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'XINGANG', 272::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'SHEKOU', 272::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'CR', 'GUANGZHOU', 272::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'SHANGHAI', 164::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'NINGBO', 216::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'QINGDAO', 216::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'XIAMEN', 221::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'SHANTOU', 221::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'DALIAN', 221::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'CHONGQING', 221::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'FUZHOU', 221::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'SHENZHEN', 226::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'XINGANG', 226::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'SHEKOU', 226::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7'),
                    (49, 'PA', 'GUANGZHOU', 226::numeric, DATE '2026-09-18', 'CNCA-024-049-v4.7')
            )
            INSERT INTO pricing."OwnLclHistoricalRates"
                (id, consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version)
            SELECT gen_random_uuid(), consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version
            FROM corrected
            ON CONFLICT (consolidation_number, destination_code, pol_code)
            DO UPDATE SET sale_per_cbm = EXCLUDED.sale_per_cbm,
                          valid_to = EXCLUDED.valid_to,
                          version = EXCLUDED.version;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."OwnLclConsolidations"
            SET booking = NULL,
                carrier_name = NULL,
                carrier_code = NULL,
                panama_arrival_port_name = NULL,
                panama_arrival_port_code = NULL,
                updated_at_utc = now()
            WHERE consolidation_number IN (48, 49);
            """);
    }
}

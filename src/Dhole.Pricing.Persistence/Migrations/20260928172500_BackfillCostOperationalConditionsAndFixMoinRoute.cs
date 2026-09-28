using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260928172500_BackfillCostOperationalConditionsAndFixMoinRoute")]
public sealed class BackfillCostOperationalConditionsAndFixMoinRoute : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Idempotent backfill for environments that already ran the first
            -- operational-conditions migration before all existing optional rows
            -- had been normalized.
            UPDATE pricing."Costs"
            SET operational_conditions = ARRAY_REMOVE(
                ARRAY[
                    CASE
                        WHEN lower(name) LIKE '%carga peligrosa%'
                          OR lower(name) LIKE '%dangerous%'
                          OR lower(name) LIKE '%hazmat%'
                        THEN 'DangerousCargo'
                    END,
                    CASE
                        WHEN lower(name) LIKE '%sobrepeso%'
                          OR lower(name) LIKE '%sobre peso%'
                          OR lower(name) LIKE '%3 ejes%'
                        THEN 'Overweight'
                    END,
                    CASE
                        WHEN lower(name) LIKE '%merchant%'
                        THEN 'MerchantHaulage'
                    END,
                    CASE
                        WHEN lower(name) LIKE '%naviera%'
                          OR lower(name) LIKE '%carrier haulage%'
                        THEN 'CarrierHaulage'
                    END,
                    CASE
                        WHEN lower(name) LIKE '%retiro vacio%'
                          OR lower(name) LIKE '%retiro de vacio%'
                          OR lower(name) LIKE '%empty return%'
                        THEN 'EmptyReturn'
                    END,
                    CASE
                        WHEN lower(name) LIKE '%marchamo electr%'
                          OR lower(name) LIKE '%electronic seal%'
                        THEN 'ElectronicSeal'
                    END,
                    CASE
                        WHEN lower(name) LIKE '%anticipado%'
                        THEN 'Anticipado'
                    END,
                    CASE
                        WHEN lower(name) LIKE '%redestino%'
                        THEN 'Redestino'
                    END
                ]::text[],
                NULL
            )
            WHERE cost_type = 'Optional'
              AND cardinality(operational_conditions) = 0;

            -- Inland Moin is mutually exclusive with Caldera. Older records could
            -- have both POEs selected, which made /costs/select legitimately return
            -- Moin charges for a Caldera quote. Resolve the canonical Moin POE from
            -- the existing multi-POE selections, then keep only that POE for these
            -- explicitly named Moin inland charges.
            DO $$
            DECLARE
                moin_poe_id uuid;
            BEGIN
                SELECT selection.port_id
                INTO moin_poe_id
                FROM pricing."CostRoutePortSelections" selection
                INNER JOIN pricing."Costs" cost ON cost.id = selection.cost_id
                WHERE selection.role = 'Poe'
                  AND lower(cost.name) LIKE 'inland moin a gam%'
                  AND selection.port_id <> COALESCE(cost.poe_id, '00000000-0000-0000-0000-000000000000'::uuid)
                ORDER BY selection.port_id
                LIMIT 1;

                IF moin_poe_id IS NOT NULL THEN
                    DELETE FROM pricing."CostRoutePortSelections" selection
                    USING pricing."Costs" cost
                    WHERE selection.cost_id = cost.id
                      AND selection.role = 'Poe'
                      AND lower(cost.name) LIKE 'inland moin a gam%'
                      AND NOT cost.is_deleted;

                    INSERT INTO pricing."CostRoutePortSelections" (cost_id, role, port_id)
                    SELECT cost.id, 'Poe', moin_poe_id
                    FROM pricing."Costs" cost
                    WHERE lower(cost.name) LIKE 'inland moin a gam%'
                      AND NOT cost.is_deleted
                    ON CONFLICT DO NOTHING;
                END IF;
            END $$;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data normalization is intentionally not reversed.
    }
}

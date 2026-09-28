using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260928170000_AddCostOperationalConditions")]
public sealed class AddCostOperationalConditions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."Costs"
                ADD COLUMN IF NOT EXISTS operational_conditions text[] NOT NULL
                DEFAULT ARRAY[]::text[];

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

            -- Corrige los cargos Inland Moin que históricamente quedaron asociados
            -- también a Caldera. La selección multi-POE es la fuente autoritativa.
            DO $$
            DECLARE
                moin_poe_id uuid;
            BEGIN
                SELECT selection.port_id
                INTO moin_poe_id
                FROM pricing."CostRoutePortSelections" selection
                INNER JOIN pricing."Costs" cost ON cost.id = selection.cost_id
                WHERE selection.role = 'Poe'
                  AND lower(cost.name) LIKE '%moin%'
                  AND (cost.poe_id IS NULL OR selection.port_id <> cost.poe_id)
                LIMIT 1;

                IF moin_poe_id IS NOT NULL THEN
                    DELETE FROM pricing."CostRoutePortSelections" selection
                    USING pricing."Costs" cost
                    WHERE selection.cost_id = cost.id
                      AND selection.role = 'Poe'
                      AND lower(cost.name) LIKE 'inland moin a gam%';

                    INSERT INTO pricing."CostRoutePortSelections" (cost_id, role, port_id)
                    SELECT id, 'Poe', moin_poe_id
                    FROM pricing."Costs"
                    WHERE lower(name) LIKE 'inland moin a gam%'
                      AND NOT is_deleted
                    ON CONFLICT DO NOTHING;
                END IF;
            END $$;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."Costs"
                DROP COLUMN IF EXISTS operational_conditions;
            """
        );
    }
}

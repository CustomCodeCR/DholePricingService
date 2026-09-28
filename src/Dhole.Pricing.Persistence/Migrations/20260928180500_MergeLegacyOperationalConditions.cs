using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260928180500_MergeLegacyOperationalConditions")]
public sealed class MergeLegacyOperationalConditions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Transitional data repair only. Runtime selection no longer infers button
            -- behavior from the cost name; Costos y recargos remains the source of truth.
            -- This merges missing button tags into legacy Optional rows without deleting
            -- conditions that Pricing users already configured explicitly.
            UPDATE pricing."Costs" AS cost
            SET operational_conditions = (
                SELECT COALESCE(
                    array_agg(DISTINCT condition ORDER BY condition)
                        FILTER (WHERE condition IS NOT NULL),
                    ARRAY[]::text[]
                )
                FROM unnest(
                    COALESCE(cost.operational_conditions, ARRAY[]::text[])
                    ||
                    ARRAY[
                        CASE
                            WHEN lower(cost.name) LIKE '%carga peligrosa%'
                              OR lower(cost.name) LIKE '%dangerous%'
                              OR lower(cost.name) LIKE '%hazmat%'
                            THEN 'DangerousCargo'
                        END,
                        CASE
                            WHEN lower(cost.name) LIKE '%sobrepeso%'
                              OR lower(cost.name) LIKE '%sobre peso%'
                              OR lower(cost.name) LIKE '%3 ejes%'
                            THEN 'Overweight'
                        END,
                        CASE
                            WHEN lower(cost.name) LIKE '%merchant%'
                              OR lower(cost.name) LIKE '%gate in%'
                            THEN 'MerchantHaulage'
                        END,
                        CASE
                            WHEN lower(cost.name) LIKE '%naviera%'
                              OR lower(cost.name) LIKE '%carrier haulage%'
                            THEN 'CarrierHaulage'
                        END,
                        CASE
                            WHEN lower(cost.name) LIKE '%retiro vacio%'
                              OR lower(cost.name) LIKE '%retiro de vacio%'
                              OR lower(cost.name) LIKE '%empty return%'
                            THEN 'EmptyReturn'
                        END,
                        CASE
                            WHEN lower(cost.name) LIKE '%marchamo electr%'
                              OR lower(cost.name) LIKE '%electronic seal%'
                            THEN 'ElectronicSeal'
                        END,
                        CASE
                            WHEN lower(cost.name) LIKE '%anticipado%'
                            THEN 'Anticipado'
                        END,
                        CASE
                            WHEN lower(cost.name) LIKE '%redestino%'
                            THEN 'Redestino'
                        END
                    ]::text[]
                ) AS condition
            )
            WHERE cost.cost_type = 'Optional'
              AND NOT cost.is_deleted;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data backfill is intentionally not reversed.
    }
}
